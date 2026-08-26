using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Adapters.ObjectStore.S3;

/// <summary>How the composition root configures the object-store backing, in primitives only.</summary>
/// <param name="ServiceUrl">The endpoint URL.</param>
/// <param name="Region">The region name.</param>
/// <param name="ForcePathStyle">Whether to address buckets by path — true for the local stack.</param>
/// <param name="AccessKey">The application account's access key.</param>
/// <param name="SecretKey">The application account's secret.</param>
/// <param name="Bucket">The bucket battle logs live in.</param>
public sealed record S3ObjectStoreOptions(
    string ServiceUrl,
    string Region,
    bool ForcePathStyle,
    string AccessKey,
    string SecretKey,
    string Bucket);

/// <summary>The S3-backed <see cref="IBattleLogStore"/>: durable put/get, gzip at rest, the id mapped to an object name internally.</summary>
public sealed class S3BattleLogStore : IBattleLogStore, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;

    private S3BattleLogStore(AmazonS3Client client, string bucket)
    {
        _client = client;
        _bucket = bucket;
    }

    /// <summary>Opens the store. The composition root hands this primitives and never sees a vendor type.</summary>
    /// <param name="options">The backing's configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">A required option is blank.</exception>
    public static S3BattleLogStore Create(S3ObjectStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequireText(options.ServiceUrl, nameof(options.ServiceUrl));
        RequireText(options.AccessKey, nameof(options.AccessKey));
        RequireText(options.SecretKey, nameof(options.SecretKey));
        RequireText(options.Bucket, nameof(options.Bucket));

        var client = new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                AuthenticationRegion = options.Region,
                ForcePathStyle = options.ForcePathStyle,
            });

        return new S3BattleLogStore(client, options.Bucket);
    }

    /// <summary>The object name a log is stored under — the whole of the id-to-storage mapping, decided once.</summary>
    /// <param name="id">The log's identity.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is a default struct with no text.</exception>
    public static string StorageNameOf(BattleLogId id) =>
        id.Value is null
            ? throw new ArgumentException(
                "default(BattleLogId) carries no text — a store that keyed on it would file every " +
                "such log under one name.",
                nameof(id))
            : id.Value + ".gz";

    /// <inheritdoc/>
    public async Task PutAsync(BattleLogId id, ReadOnlyMemory<byte> log, CancellationToken ct)
    {
        var name = StorageNameOf(id);
        ct.ThrowIfCancellationRequested();

        using var body = new MemoryStream(BattleLogCompression.Compress(log), writable: false);

        try
        {
            await _client.PutObjectAsync(
                    new PutObjectRequest
                    {
                        BucketName = _bucket,
                        Key = name,
                        InputStream = body,
                        AutoCloseStream = false,
                    },
                    ct)
                .ConfigureAwait(false);
        }
        catch (AmazonClientException vendor)
        {
            throw Unreachable(vendor);
        }
    }

    /// <inheritdoc/>
    public async Task<ReadOnlyMemory<byte>?> GetAsync(BattleLogId id, CancellationToken ct)
    {
        var name = StorageNameOf(id);
        ct.ThrowIfCancellationRequested();

        try
        {
            using var response = await _client
                .GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = name }, ct)
                .ConfigureAwait(false);

            using var stored = new MemoryStream();
            await response.ResponseStream.CopyToAsync(stored, ct).ConfigureAwait(false);

            return BattleLogCompression.Decompress(stored.ToArray());
        }
        catch (AmazonS3Exception missing) when (
            missing.StatusCode == HttpStatusCode.NotFound &&
            missing.ErrorCode is "NoSuchKey" or "NotFound")
        {
            // A vendor fault translated at the edge: an absent OBJECT is the port's null. A missing
            // BUCKET is also a 404 but is a broken deployment, and answering "no logs exist" for it
            // would hide the misconfiguration forever — it falls through to the loud translation.
            return null;
        }
        catch (AmazonClientException vendor)
        {
            throw Unreachable(vendor);
        }
    }

    /// <summary>Every non-absence vendor fault, translated: the application catches a BCL type, never the vendor's.</summary>
    private static InvalidOperationException Unreachable(AmazonClientException vendor) =>
        new(
            "The battle-log store did not take the call: " + vendor.Message + ". A replay that " +
            "cannot be read is a replay that cannot be offered — battle-log trouble never fails " +
            "a command.",
            vendor);

    /// <inheritdoc/>
    public void Dispose() => _client.Dispose();

    private static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A blank " + name + " is a deployment that forgot one of the ObjectStore:* " +
                "variables — fail here, where the missing one is nameable.",
                name);
        }
    }
}
