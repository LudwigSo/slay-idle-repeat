using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SlayIdleRepeat.Architecture.Tests.Infrastructure;

/// <summary>
/// Metadata and IL helpers. Every "X must not name Y" rule in this suite is
/// answered here rather than by a text grep, because a grep is defeated by a
/// <c>using</c> alias, a fully-qualified call or an extension method, and it
/// produces false positives inside comments and string literals.
/// </summary>
internal static class Il
{
    /// <summary>Every type in a module, including nested types, excluding the module pseudo-type.</summary>
    internal static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module) =>
        module.Types.Where(t => t.FullName != "<Module>").SelectMany(WithNested);

    /// <summary>Types declared directly in the given namespace or any namespace below it.</summary>
    internal static IEnumerable<TypeDefinition> TypesUnder(ModuleDefinition module, string namespacePrefix) =>
        AllTypes(module).Where(t => IsUnder(NamespaceOf(t), namespacePrefix));

    /// <summary>True when <paramref name="ns"/> is <paramref name="prefix"/> or a namespace below it.</summary>
    internal static bool IsUnder(string ns, string prefix) =>
        ns.Equals(prefix, StringComparison.Ordinal) ||
        ns.StartsWith(prefix + ".", StringComparison.Ordinal);

    /// <summary>The namespace of a type, walking out to the outermost declaring type for nested types.</summary>
    internal static string NamespaceOf(TypeDefinition type)
    {
        var outer = type;
        while (outer.DeclaringType is not null)
        {
            outer = outer.DeclaringType;
        }

        return outer.Namespace ?? string.Empty;
    }

    /// <summary>Every method, constructor and accessor declared on a type.</summary>
    internal static IEnumerable<MethodDefinition> AllMethods(TypeDefinition type) => type.Methods;

    /// <summary>The IL instructions of a method, or nothing when it has no body (abstract, extern, interface).</summary>
    internal static IEnumerable<Instruction> Instructions(MethodDefinition method) =>
        method.HasBody ? method.Body.Instructions : Enumerable.Empty<Instruction>();

    /// <summary><c>Namespace.Type.Member</c>, for failure messages that name the offender.</summary>
    internal static string Describe(MethodDefinition method) =>
        $"{method.DeclaringType.FullName}.{method.Name}";

    /// <summary><c>Namespace.Type.field</c>, for failure messages that name the offender.</summary>
    internal static string Describe(FieldDefinition field) =>
        $"{field.DeclaringType.FullName}.{field.Name}";

    /// <summary>
    /// Every type full-name a type mentions anywhere: base type, interfaces, field,
    /// property and event types, method signatures, local variables, attributes and
    /// every type, method and field referenced from its IL. Generic arguments, array
    /// element types and by-ref types are flattened.
    /// </summary>
    internal static IEnumerable<string> ReferencedTypeNames(TypeDefinition type)
    {
        foreach (var name in ReferencedTypeReferences(type).Select(r => r.FullName).Distinct(StringComparer.Ordinal))
        {
            yield return name;
        }
    }

    /// <summary>The unflattened companion of <see cref="ReferencedTypeNames"/>, flattened through generics.</summary>
    internal static IEnumerable<TypeReference> ReferencedTypeReferences(TypeDefinition type)
    {
        foreach (var reference in Raw(type).SelectMany(Flatten))
        {
            yield return reference;
        }

        static IEnumerable<TypeReference?> Raw(TypeDefinition type)
        {
            yield return type.BaseType;

            foreach (var i in type.Interfaces)
            {
                yield return i.InterfaceType;
            }

            foreach (var attribute in AttributeTypes(type.CustomAttributes))
            {
                yield return attribute;
            }

            foreach (var field in type.Fields)
            {
                yield return field.FieldType;

                foreach (var attribute in AttributeTypes(field.CustomAttributes))
                {
                    yield return attribute;
                }
            }

            foreach (var property in type.Properties)
            {
                yield return property.PropertyType;
            }

            foreach (var @event in type.Events)
            {
                yield return @event.EventType;
            }

            foreach (var method in type.Methods)
            {
                foreach (var reference in SignatureTypes(method))
                {
                    yield return reference;
                }

                foreach (var attribute in AttributeTypes(method.CustomAttributes))
                {
                    yield return attribute;
                }

                if (method.HasBody)
                {
                    foreach (var local in method.Body.Variables)
                    {
                        yield return local.VariableType;
                    }

                    foreach (var reference in method.Body.Instructions.SelectMany(OperandTypes))
                    {
                        yield return reference;
                    }
                }
            }
        }
    }

    /// <summary>Return type, parameter types and generic constraints of a method.</summary>
    internal static IEnumerable<TypeReference?> SignatureTypes(MethodDefinition method)
    {
        yield return method.ReturnType;

        foreach (var parameter in method.Parameters)
        {
            yield return parameter.ParameterType;
        }

        foreach (var constraint in method.GenericParameters.SelectMany(g => g.Constraints))
        {
            yield return constraint.ConstraintType;
        }
    }

    /// <summary>Every type an instruction's operand mentions.</summary>
    internal static IEnumerable<TypeReference?> OperandTypes(Instruction instruction)
    {
        switch (instruction.Operand)
        {
            case TypeReference type:
                yield return type;
                break;
            case MethodReference method:
                yield return method.DeclaringType;
                yield return method.ReturnType;

                foreach (var parameter in method.Parameters)
                {
                    yield return parameter.ParameterType;
                }

                break;
            case FieldReference field:
                yield return field.DeclaringType;
                yield return field.FieldType;
                break;
        }
    }

    /// <summary>Expands a type reference through generics, arrays, pointers and by-ref wrappers.</summary>
    internal static IEnumerable<TypeReference> Flatten(TypeReference? reference)
    {
        if (reference is null)
        {
            yield break;
        }

        yield return reference;

        if (reference is GenericInstanceType generic)
        {
            foreach (var argument in generic.GenericArguments.SelectMany(Flatten))
            {
                yield return argument;
            }

            foreach (var element in Flatten(generic.ElementType))
            {
                yield return element;
            }
        }
        else if (reference is TypeSpecification specification)
        {
            foreach (var element in Flatten(specification.ElementType))
            {
                yield return element;
            }
        }
    }

    /// <summary>The assembly a type reference resolves to, by metadata scope. Empty when unknown.</summary>
    internal static string AssemblyNameOf(TypeReference reference)
    {
        var scope = reference.Scope;
        return scope switch
        {
            AssemblyNameReference assembly => assembly.Name,
            ModuleDefinition module => module.Assembly?.Name.Name ?? string.Empty,
            ModuleReference module => module.Name,
            _ => reference.Module?.Assembly?.Name.Name ?? string.Empty,
        };
    }

    /// <summary>True for the BCL: <c>System.*</c>, <c>mscorlib</c>, <c>netstandard</c>.</summary>
    internal static bool IsBclAssembly(string assemblyName) =>
        assemblyName.Equals("System", StringComparison.Ordinal) ||
        assemblyName.StartsWith("System.", StringComparison.Ordinal) ||
        assemblyName.Equals("mscorlib", StringComparison.Ordinal) ||
        assemblyName.Equals("netstandard", StringComparison.Ordinal);

    /// <summary>Every method that references a member of the named type, anywhere in its IL.</summary>
    internal static IEnumerable<MethodDefinition> MethodsWithBodies(ModuleDefinition module) =>
        AllTypes(module).SelectMany(AllMethods).Where(m => m.HasBody);

    private static IEnumerable<TypeReference?> AttributeTypes(IEnumerable<CustomAttribute> attributes)
    {
        foreach (var attribute in attributes)
        {
            yield return attribute.AttributeType;
        }
    }

    private static IEnumerable<TypeDefinition> WithNested(TypeDefinition type)
    {
        yield return type;

        foreach (var nested in type.NestedTypes.SelectMany(WithNested))
        {
            yield return nested;
        }
    }
}
