﻿/*
    Copyright 2017-2019 Katy Coe - http://www.hearthcode.org - http://www.djkaty.com

    All rights reserved.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Il2CppInspector.Reflection {
    public class TypeInfo : MemberInfo
    {
        // IL2CPP-specific data
        public Il2CppTypeDefinition Definition { get; }
        public int Index { get; } = -1;

        // Information/flags about the type
        // Undefined if the Type represents a generic type parameter
        public TypeAttributes Attributes { get; }

        // Type that this type inherits from
        private readonly int baseTypeUsage = -1;

        public TypeInfo BaseType => IsPointer? null :
            baseTypeUsage != -1?
                Assembly.Model.GetTypeFromUsage(baseTypeUsage, MemberTypes.TypeInfo)
                : IsArray? Assembly.Model.TypesByFullName["System.Array"]
                : Namespace != "System" || BaseName != "Object" ? Assembly.Model.TypesByFullName["System.Object"]
                : null;

        // True if the type contains unresolved generic type parameters
        public bool ContainsGenericParameters { get; }

        public string BaseName => base.Name;

        // Get rid of generic backticks
        public string UnmangledBaseName => base.Name.IndexOf("`", StringComparison.Ordinal) == -1 ? base.Name : base.Name.Remove(base.Name.IndexOf("`", StringComparison.Ordinal));

        // C# colloquial name of the type (if available)
        public string CSharpName {
            get {
                var s = Namespace + "." + base.Name;
                var i = Il2CppConstants.FullNameTypeString.IndexOf(s);
                var n = (i != -1 ? Il2CppConstants.CSharpTypeString[i] : base.Name);
                if (n?.IndexOf("`", StringComparison.Ordinal) != -1)
                    n = n?.Remove(n.IndexOf("`", StringComparison.Ordinal));
                var g = (GenericTypeParameters != null ? "<" + string.Join(", ", GenericTypeParameters.Select(x => x.CSharpName)) + ">" : "");
                g = (GenericTypeArguments != null ? "<" + string.Join(", ", GenericTypeArguments.Select(x => x.CSharpName)) + ">" : g);
                n += g;
                if (s == "System.Nullable`1" && GenericTypeArguments.Any())
                    n = GenericTypeArguments[0].CSharpName + "?";
                if (HasElementType)
                    n = ElementType.CSharpName;
                return n + (IsArray ? "[" + new string(',', GetArrayRank() - 1) + "]" : "") + (IsPointer ? "*" : "");
            }
        }

        // C# name as it would be written in a type declaration
        public string CSharpTypeDeclarationName =>
                    (HasElementType?
                        ElementType.CSharpTypeDeclarationName :
                        base.Name.IndexOf("`", StringComparison.Ordinal) == -1 ? base.Name : base.Name.Remove(base.Name.IndexOf("`", StringComparison.Ordinal))
                        + (GenericTypeParameters != null ? "<" + string.Join(", ", GenericTypeParameters.Select(x => x.Name)) + ">" : "")
                        + (GenericTypeArguments != null ? "<" + string.Join(", ", GenericTypeArguments.Select(x => x.Name)) + ">" : ""))
                   + (IsArray ? "[" + new string(',', GetArrayRank() - 1) + "]" : "")
                   + (IsPointer ? "*" : "");

        // Custom attributes for this member
        public override IEnumerable<CustomAttributeData> CustomAttributes => CustomAttributeData.GetCustomAttributes(this);

        public List<ConstructorInfo> DeclaredConstructors { get; } = new List<ConstructorInfo>();
        public List<EventInfo> DeclaredEvents { get; } = new List<EventInfo>();
        public List<FieldInfo> DeclaredFields { get; } = new List<FieldInfo>();

        public List<MemberInfo> DeclaredMembers => new IEnumerable<MemberInfo>[] {
            DeclaredConstructors, DeclaredEvents, DeclaredFields, DeclaredMethods,
            DeclaredNestedTypes?.ToList() ?? new List<TypeInfo>(), DeclaredProperties
        }.SelectMany(m => m).ToList();

        public List<MethodInfo> DeclaredMethods { get; } = new List<MethodInfo>();

        private int[] declaredNestedTypes;
        public IEnumerable<TypeInfo> DeclaredNestedTypes => declaredNestedTypes.Select(x => Assembly.Model.TypesByDefinitionIndex[x]);

        public List<PropertyInfo> DeclaredProperties { get; } = new List<PropertyInfo>();

        // Get a field by its name
        public FieldInfo GetField(string name) => DeclaredFields.FirstOrDefault(f => f.Name == name);

        private readonly int genericConstraintIndex;

        private readonly int genericConstraintCount;

        // Get type constraints on a generic parameter
        public TypeInfo[] GetGenericParameterConstraints() {
            var types = new TypeInfo[genericConstraintCount];
            for (int c = 0; c < genericConstraintCount; c++)
                types[c] = Assembly.Model.GetTypeFromUsage(Assembly.Model.Package.GenericConstraintIndices[genericConstraintIndex + c], MemberTypes.TypeInfo);
            return types;
        }

        // Get a method by its name
        public MethodInfo GetMethod(string name) => DeclaredMethods.FirstOrDefault(m => m.Name == name);

        // Get all methods with same name (overloads)
        public MethodInfo[] GetMethods(string name) => DeclaredMethods.Where(m => m.Name == Name).ToArray();

        // Get methods including inherited methods
        public MethodInfo[] GetAllMethods() {
            var methods = new List<IEnumerable<MethodInfo>>();

            for (var type = this; type != null; type = type.BaseType)
                methods.Add(type.DeclaredMethods);

            return methods.SelectMany(m => m).ToArray();
        }

        // Get a property by its name
        public PropertyInfo GetProperty(string name) => DeclaredProperties.FirstOrDefault(p => p.Name == name);

        // Method that the type is declared in if this is a type parameter of a generic method
        // TODO: Make a unit test from this: https://docs.microsoft.com/en-us/dotnet/api/system.type.declaringmethod?view=netframework-4.8
        public MethodBase DeclaringMethod;
        
        // IsGenericTypeParameter and IsGenericMethodParameter from https://github.com/dotnet/corefx/issues/23883
        public bool IsGenericTypeParameter => IsGenericParameter && DeclaringMethod == null;
        public bool IsGenericMethodParameter => IsGenericParameter && DeclaringMethod != null;

        // Gets the type of the object encompassed or referred to by the current array, pointer or reference type
        public TypeInfo ElementType { get; }

        // Type name including namespace
        public string FullName =>
            IsGenericParameter? null :
                (HasElementType? ElementType.FullName : 
                    (DeclaringType != null? DeclaringType.FullName + "+" : Namespace + (Namespace.Length > 0? "." : ""))
                    + base.Name
                    + (GenericTypeParameters != null ? "[" + string.Join(",", GenericTypeParameters.Select(x => x.FullName ?? x.Name)) + "]" : "")
                    + (GenericTypeArguments != null ? "[" + string.Join(",", GenericTypeArguments.Select(x => x.FullName ?? x.Name)) + "]" : ""))
                + (IsArray? "[" + new string(',', GetArrayRank() - 1) + "]" : "")
                + (IsPointer? "*" : "");

        // Returns the minimally qualified type name required to refer to this type within the specified scope
        public string GetScopedFullName(Scope scope) {
            // This is the scope in which this type is currently being used
            // If Scope.Current is null, our scope is at the assembly level
            var usingScope = scope.Current?.FullName ?? "";

            // This is the scope in which this type's definition is located
            var declaringScope = DeclaringType?.FullName ?? Namespace;

            // If the scope of usage is inside the scope in which the type is declared, no additional scope is needed
            if ((usingScope + ".").StartsWith(declaringScope + ".") || (usingScope + "+").StartsWith(declaringScope + "+"))
                return base.Name;

            // Global (unnamed) namespace?
            string scopedName;
            if (string.IsNullOrEmpty(declaringScope))
                scopedName = base.Name;

            // Find first difference in the declaring scope from the using scope, moving one namespace/type name at a time
            else {
                var diff = 0;

                usingScope += ".";
                while (usingScope.IndexOf(".", diff) == declaringScope.IndexOf(".", diff)
                       && usingScope.IndexOf(".", diff) != -1
                       && usingScope.Substring(0, usingScope.IndexOf(".", diff))
                       == declaringScope.Substring(0, declaringScope.IndexOf(".", diff)))
                    diff = usingScope.IndexOf(".", diff) + 1;

                usingScope = usingScope.Substring(0, usingScope.Length -1) + "+";
                while (usingScope.IndexOf("+", diff) == declaringScope.IndexOf("+", diff)
                       && usingScope.IndexOf("+", diff) != -1
                       && usingScope.Substring(0, usingScope.IndexOf("+", diff))
                       == declaringScope.Substring(0, declaringScope.IndexOf("+", diff)))
                    diff = usingScope.IndexOf("+", diff) + 1;

                scopedName = declaringScope.Substring(diff) + (DeclaringType != null? "+" : ".") + base.Name;
            }

            // At this point, scopedName contains the minimum required scope, discounting any using directives
            // or whether there are conflicts with any ancestor scope

            // Check to see if there is a namespace in our using directives which brings this type into scope
            var usingRef = scope.Namespaces.OrderByDescending(n => n.Length).FirstOrDefault(n => scopedName.StartsWith(n + "."));
            var minimallyScopedName = usingRef == null ? scopedName : scopedName.Substring(usingRef.Length + 1);

            // minimallyScopedName now contains the minimum required scope, taking using directives into account

            // Are there any ancestors in the using scope with the same type name as the first part of the minimally scoped name?
            // If so, the ancestor type name will hide the type we are trying to reference,
            // so we need to provide the scope ignoring any using directives
            var firstPart = minimallyScopedName.Split('.')[0].Split('+')[0];
            for (var d = scope.Current; d != null; d = d.DeclaringType)
                if (d.BaseName == firstPart)
                    return scopedName.Replace('+', '.');

            // If there are multiple using directives that would allow the same minimally scoped name to be used,
            // then the minimally scoped name is ambiguous and we can't use it
            // NOTE: We should check all the parts, not just the first part, but this works in the vast majority of cases
            if (scope.Namespaces.Count(n => Assembly.Model.TypesByFullName.ContainsKey(n + "." + firstPart)) > 1)
                return scopedName.Replace('+', '.');

            return minimallyScopedName.Replace('+', '.');
        }

        // C#-friendly type name as it should be used in the scope of a given type
        public string GetScopedCSharpName(Scope usingScope = null) {
            // Unscoped name if no using scope specified
            if (usingScope == null)
                return CSharpName;

            var s = Namespace + "." + base.Name;

            // Built-in keyword type names do not require a scope
            var i = Il2CppConstants.FullNameTypeString.IndexOf(s);
            var n = i != -1 ? Il2CppConstants.CSharpTypeString[i] : GetScopedFullName(usingScope);

            // Unmangle generic type names
            if (n?.IndexOf("`", StringComparison.Ordinal) != -1)
                n = n?.Remove(n.IndexOf("`", StringComparison.Ordinal));

            // Generic type parameters and type arguments
            var g = (GenericTypeParameters != null ? "<" + string.Join(", ", GenericTypeParameters.Select(x => x.GetScopedCSharpName(usingScope))) + ">" : "");
            g = (GenericTypeArguments != null ? "<" + string.Join(", ", GenericTypeArguments.Select(x => x.GetScopedCSharpName(usingScope))) + ">" : g);
            n += g;

            // Nullable types
            if (s == "System.Nullable`1" && GenericTypeArguments.Any())
                n = GenericTypeArguments[0].GetScopedCSharpName(usingScope) + "?";

            // Arrays, pointers, references
            if (HasElementType)
                n = ElementType.GetScopedCSharpName(usingScope);

            return n + (IsArray ? "[" + new string(',', GetArrayRank() - 1) + "]" : "") + (IsPointer ? "*" : "");
        }

        public GenericParameterAttributes GenericParameterAttributes { get; }

        public int GenericParameterPosition { get; }

        public List<TypeInfo> GenericTypeParameters { get; }

        public List<TypeInfo> GenericTypeArguments { get; }

        // True if an array, pointer or reference, otherwise false
        // See: https://docs.microsoft.com/en-us/dotnet/api/system.type.haselementtype?view=netframework-4.8
        public bool HasElementType => ElementType != null;

        private readonly int[] implementedInterfaceUsages;
        public IEnumerable<TypeInfo> ImplementedInterfaces => implementedInterfaceUsages.Select(x => Assembly.Model.GetTypeFromUsage(x, MemberTypes.TypeInfo));

        public bool IsAbstract => (Attributes & TypeAttributes.Abstract) == TypeAttributes.Abstract;
        public bool IsArray { get; }
        public bool IsByRef => throw new NotImplementedException();
        public bool IsClass => (Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Class;
        public bool IsEnum => enumUnderlyingTypeUsage != -1;
        public bool IsGenericParameter { get; }
        public bool IsGenericType { get; }
        public bool IsGenericTypeDefinition { get; }
        public bool IsImport => (Attributes & TypeAttributes.Import) == TypeAttributes.Import;
        public bool IsInterface => (Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface;
        public bool IsNested => (MemberType & MemberTypes.NestedType) == MemberTypes.NestedType;
        public bool IsNestedAssembly => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedAssembly;
        public bool IsNestedFamANDAssem => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedFamANDAssem;
        public bool IsNestedFamily => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedFamily;
        public bool IsNestedFamORAssem => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedFamORAssem;
        public bool IsNestedPrivate => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedPrivate;
        public bool IsNestedPublic => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedPublic;
        public bool IsNotPublic => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NotPublic;
        public bool IsPointer { get; }
        // Primitive types table: https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/built-in-types-table (we exclude Object and String)
        public bool IsPrimitive => Namespace == "System" && new[] { "Boolean", "Byte", "SByte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "IntPtr", "UIntPtr", "Char", "Decimal", "Double", "Single" }.Contains(Name);
        public bool IsPublic => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public;
        public bool IsSealed => (Attributes & TypeAttributes.Sealed) == TypeAttributes.Sealed;
        public bool IsSerializable => (Attributes & TypeAttributes.Serializable) == TypeAttributes.Serializable;
        public bool IsSpecialName => (Attributes & TypeAttributes.SpecialName) == TypeAttributes.SpecialName;
        public bool IsValueType => BaseType?.FullName == "System.ValueType";

        // Helper function for determining if using this type as a field, parameter etc. requires that field or method to be declared as unsafe
        public bool RequiresUnsafeContext => IsPointer || (HasElementType && ElementType.RequiresUnsafeContext);

        // May get overridden by Il2CppType-based constructor below
        public override MemberTypes MemberType { get; } = MemberTypes.TypeInfo;

        private string @namespace;
        public string Namespace {
            get => !string.IsNullOrEmpty(@namespace) ? @namespace : DeclaringType?.Namespace ?? "";
            set => @namespace = value;
        }

        // Number of dimensions of an array
        private readonly int arrayRank;
        public int GetArrayRank() => arrayRank;

        public string[] GetEnumNames() => IsEnum? DeclaredFields.Where(x => x.Name != "value__").Select(x => x.Name).ToArray() : throw new InvalidOperationException("Type is not an enumeration");

        // The underlying type of an enumeration (int by default)
        private readonly int enumUnderlyingTypeUsage = -1;
        private TypeInfo enumUnderlyingType;

        public TypeInfo GetEnumUnderlyingType() {
            if (!IsEnum)
                return null;
            enumUnderlyingType ??= Assembly.Model.GetTypeFromUsage(enumUnderlyingTypeUsage, MemberTypes.TypeInfo);
            return enumUnderlyingType;
        }

        public Array GetEnumValues() => IsEnum? DeclaredFields.Where(x => x.Name != "value__").Select(x => x.DefaultValue).ToArray() : throw new InvalidOperationException("Type is not an enumeration");

        // Initialize from specified type index in metadata

        // Top-level types
        public TypeInfo(int typeIndex, Assembly owner) : base(owner) {
            var pkg = Assembly.Model.Package;

            Definition = pkg.TypeDefinitions[typeIndex];
            Index = typeIndex;
            Namespace = pkg.Strings[Definition.namespaceIndex];
            Name = pkg.Strings[Definition.nameIndex];

            // Derived type?
            if (Definition.parentIndex >= 0)
                baseTypeUsage = Definition.parentIndex;

            // Nested type?
            if (Definition.declaringTypeIndex >= 0) {
                declaringTypeDefinitionIndex = (int) pkg.TypeUsages[Definition.declaringTypeIndex].datapoint;
                MemberType |= MemberTypes.NestedType;
            }

            // Generic type definition?
            if (Definition.genericContainerIndex >= 0) {
                IsGenericType = true;
                IsGenericParameter = false;
                IsGenericTypeDefinition = true; // All of our generic type parameters are unresolved
                ContainsGenericParameters = true;

                // Store the generic type parameters for later instantiation
                var container = pkg.GenericContainers[Definition.genericContainerIndex];

                GenericTypeParameters = pkg.GenericParameters.Skip((int) container.genericParameterStart).Take(container.type_argc).Select(p => new TypeInfo(this, p)).ToList();
            }

            // Add to global type definition list
            Assembly.Model.TypesByDefinitionIndex[Index] = this;
            Assembly.Model.TypesByFullName[FullName] = this;

            if ((Definition.flags & Il2CppConstants.TYPE_ATTRIBUTE_SERIALIZABLE) != 0)
                Attributes |= TypeAttributes.Serializable;
            if ((Definition.flags & Il2CppConstants.TYPE_ATTRIBUTE_VISIBILITY_MASK) == Il2CppConstants.TYPE_ATTRIBUTE_PUBLIC)
                Attributes |= TypeAttributes.Public;
            if ((Definition.flags & Il2CppConstants.TYPE_ATTRIBUTE_VISIBILITY_MASK) == Il2CppConstants.TYPE_ATTRIBUTE_NOT_PUBLIC)
                Attributes |= TypeAttributes.NotPublic;
            if ((Definition.flags & Il2CppConstants.TYPE_ATTRIBUTE_VISIBILITY_MASK) == Il2CppConstants.TYPE_ATTRIBUTE_NESTED_PUBLIC)
                Attributes |= TypeAttributes.NestedPublic;
            if ((Definition.flags & Il2CppConstants.TYPE_ATTRIBUTE_VISIBILITY_MASK) == Il2CppConstants.TYPE_ATTRIBUTE_NESTED_PRIVATE)
                Attributes |= TypeAttributes.NestedPrivate;
            if ((Definition.flags & Il2CppConstants.TYPE_ATTRIBUTE_VISIBILITY_MASK) == Il2CppConstants.TYPE_ATTRIBUTE_NESTED_ASSEMBLY)
                Attributes |= TypeAttributes.NestedAssembly;
            if ((Definition.flags & Il2CppConstants.TYPE_ATTRIBUTE_VISIBILITY_MASK) == Il2CppConstants.TYPE_ATTRIBUTE_NESTED_FAMILY)
                Attributes |= TypeAttributes.NestedFamily;
            if ((Definition.flags & Il2CppConstants.TYPE_ATTRIBUTE_VISIBILITY_MASK) == Il2CppConstants.TYPE_ATTRIBUTE_NESTED_FAM_AND_ASSEM)
                Attributes |= TypeAttributes.NestedFamANDAssem;
            if ((Definition.flags & Il2CppConstants.TYPE_ATTRIBUTE_VISIBILITY_MASK) == Il2CppConstants.TYPE_ATTRIBUTE_NESTED_FAM_OR_ASSEM)
                Attributes |= TypeAttributes.NestedFamORAssem;
            if ((Definition.flags & Il2CppConstants.TYPE_ATTRIBUTE_ABSTRACT) != 0)
                Attributes |= TypeAttributes.Abstract;
            if ((Definition.flags & Il2CppConstants.TYPE_ATTRIBUTE_SEALED) != 0)
                Attributes |= TypeAttributes.Sealed;
            if ((Definition.flags & Il2CppConstants.TYPE_ATTRIBUTE_SPECIAL_NAME) != 0)
                Attributes |= TypeAttributes.SpecialName;
            if ((Definition.flags & Il2CppConstants.TYPE_ATTRIBUTE_IMPORT) != 0)
                Attributes |= TypeAttributes.Import;

            // TypeAttributes.Class == 0 so we only care about setting TypeAttributes.Interface (it's a non-interface class by default)
            if ((Definition.flags & Il2CppConstants.TYPE_ATTRIBUTE_INTERFACE) != 0)
                Attributes |= TypeAttributes.Interface;

            // Enumerations - bit 1 of bitfield indicates this (also the baseTypeUsage will be System.Enum)
            if (((Definition.bitfield >> 1) & 1) == 1)
                enumUnderlyingTypeUsage = Definition.elementTypeIndex;

            // Add all implemented interfaces
            implementedInterfaceUsages = new int[Definition.interfaces_count];
            for (var i = 0; i < Definition.interfaces_count; i++)
                implementedInterfaceUsages[i] = pkg.InterfaceUsageIndices[Definition.interfacesStart + i];

            // Add all nested types
            declaredNestedTypes = new int[Definition.nested_type_count];
            for (var n = 0; n < Definition.nested_type_count; n++)
                declaredNestedTypes[n] = pkg.NestedTypeIndices[Definition.nestedTypesStart + n];

            // Add all fields
            for (var f = Definition.fieldStart; f < Definition.fieldStart + Definition.field_count; f++)
                DeclaredFields.Add(new FieldInfo(pkg, f, this));

            // Add all methods
            for (var m = Definition.methodStart; m < Definition.methodStart + Definition.method_count; m++) {
                var method = new MethodInfo(pkg, m, this);
                if (method.Name == ConstructorInfo.ConstructorName || method.Name == ConstructorInfo.TypeConstructorName)
                    DeclaredConstructors.Add(new ConstructorInfo(pkg, m, this));
                else
                    DeclaredMethods.Add(method);
            }

            // Add all properties
            for (var p = Definition.propertyStart; p < Definition.propertyStart + Definition.property_count; p++)
                DeclaredProperties.Add(new PropertyInfo(pkg, p, this));

            // Add all events
            for (var e = Definition.eventStart; e < Definition.eventStart + Definition.event_count; e++)
                DeclaredEvents.Add(new EventInfo(pkg, e, this));
        }

        // Initialize type from binary usage
        // Much of the following is adapted from il2cpp::vm::Class::FromIl2CppType
        public TypeInfo(Il2CppModel model, Il2CppType pType, MemberTypes memberType) {
            var image = model.Package.BinaryImage;

            // Generic type unresolved and concrete instance types
            if (pType.type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST) {
                var generic = image.ReadMappedObject<Il2CppGenericClass>(pType.datapoint); // Il2CppGenericClass *
                var genericTypeDef = model.TypesByDefinitionIndex[generic.typeDefinitionIndex];

                Definition = model.Package.TypeDefinitions[generic.typeDefinitionIndex];
                Index = (int) generic.typeDefinitionIndex;

                Assembly = genericTypeDef.Assembly;
                Namespace = genericTypeDef.Namespace;
                Name = genericTypeDef.BaseName;
                Attributes |= TypeAttributes.Class;

                // Derived type?
                if (genericTypeDef.Definition.parentIndex >= 0)
                    baseTypeUsage = genericTypeDef.Definition.parentIndex;

                // Nested type?
                if (genericTypeDef.Definition.declaringTypeIndex >= 0) {
                    declaringTypeDefinitionIndex = (int)model.Package.TypeUsages[genericTypeDef.Definition.declaringTypeIndex].datapoint;
                    MemberType = memberType | MemberTypes.NestedType;
                }

                IsGenericType = true;
                IsGenericParameter = false;
                IsGenericTypeDefinition = false; // This is a use of a generic type definition
                ContainsGenericParameters = true;

                // Get the instantiation
                var genericInstance = image.ReadMappedObject<Il2CppGenericInst>(generic.context.class_inst);

                // Get list of pointers to type parameters (both unresolved and concrete)
                var genericTypeArguments = image.ReadMappedWordArray(genericInstance.type_argv, (int)genericInstance.type_argc);

                GenericTypeArguments = new List<TypeInfo>();

                foreach (var pArg in genericTypeArguments) {
                    var argType = model.GetTypeFromVirtualAddress((ulong) pArg);
                    // TODO: Detect whether unresolved or concrete (add concrete to GenericTypeArguments instead)
                    // TODO: GenericParameterPosition etc. in types we generate here
                    // TODO: Assembly etc.
                    GenericTypeArguments.Add(argType); // TODO: Fix MemberType here
                }
            }

            // TODO: Set DeclaringType for the two below

            // Array with known dimensions and bounds
            if (pType.type == Il2CppTypeEnum.IL2CPP_TYPE_ARRAY) {
                var descriptor = image.ReadMappedObject<Il2CppArrayType>(pType.datapoint);
                ElementType = model.GetTypeFromVirtualAddress(descriptor.etype);

                Assembly = ElementType.Assembly;
                Definition = ElementType.Definition;
                Index = ElementType.Index;
                Namespace = ElementType.Namespace;
                Name = ElementType.Name;
                ContainsGenericParameters = ElementType.ContainsGenericParameters;

                IsArray = true;
                arrayRank = descriptor.rank;
            }

            // Dynamically allocated array or pointer type
            if (pType.type == Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY || pType.type == Il2CppTypeEnum.IL2CPP_TYPE_PTR) {
                ElementType = model.GetTypeFromVirtualAddress(pType.datapoint);

                Assembly = ElementType.Assembly;
                Definition = ElementType.Definition;
                Index = ElementType.Index;
                Namespace = ElementType.Namespace;
                Name = ElementType.Name;
                ContainsGenericParameters = ElementType.ContainsGenericParameters;

                IsPointer = (pType.type == Il2CppTypeEnum.IL2CPP_TYPE_PTR);
                IsArray = !IsPointer;

                // Heap arrays always have one dimension
                arrayRank = 1;
            }

            // Generic type parameter
            if (pType.type == Il2CppTypeEnum.IL2CPP_TYPE_VAR || pType.type == Il2CppTypeEnum.IL2CPP_TYPE_MVAR) {
                var paramType = model.Package.GenericParameters[pType.datapoint]; // genericParameterIndex
                var container = model.Package.GenericContainers[paramType.ownerIndex];

                var ownerType = model.TypesByDefinitionIndex[
                        container.is_method == 1
                        ? model.Package.Methods[container.ownerIndex].declaringType
                        : container.ownerIndex];

                Assembly = ownerType.Assembly;
                Namespace = "";
                Name = model.Package.Strings[paramType.nameIndex];
                Attributes |= TypeAttributes.Class;

                // Derived type?
                if (ownerType.Definition.parentIndex >= 0)
                    baseTypeUsage = ownerType.Definition.parentIndex;

                // Nested type always - sets DeclaringType used below
                declaringTypeDefinitionIndex = ownerType.Index;
                MemberType = memberType | MemberTypes.NestedType;

                // All generic method type parameters have a declared method
                if (container.is_method == 1)
                    DeclaringMethod = model.MethodsByDefinitionIndex[container.ownerIndex];

                IsGenericParameter = true;
                ContainsGenericParameters = true;
                IsGenericType = false;
                IsGenericTypeDefinition = false;
            }
        }

        // Initialize a type that is a generic parameter of a generic type
        // See: https://docs.microsoft.com/en-us/dotnet/api/system.type.isgenerictype?view=netframework-4.8
        public TypeInfo(TypeInfo declaringType, Il2CppGenericParameter param) : base(declaringType) {
            // Same visibility attributes as declaring type
            Attributes = declaringType.Attributes;

            // Same namespace as declaring type
            Namespace = declaringType.Namespace;

            // Special constraints
            GenericParameterAttributes = (GenericParameterAttributes) param.flags;

            // Type constraints
            genericConstraintIndex = param.constraintsStart;
            genericConstraintCount = param.constraintsCount;

            // Base type of object (set by default)
            // TODO: BaseType should be set to base type constraint
            // TODO: ImplementedInterfaces should be set to interface types constraints

            // Name of parameter
            Name = Assembly.Model.Package.Strings[param.nameIndex];

            // Position
            GenericParameterPosition = param.num;

            IsGenericParameter = true;
            IsGenericType = false;
            IsGenericTypeDefinition = false;
            ContainsGenericParameters = true;
        }

        // Initialize a type that is a generic parameter of a generic method
        public TypeInfo(MethodBase declaringMethod, Il2CppGenericParameter param) : this(declaringMethod.DeclaringType, param) {
            DeclaringMethod = declaringMethod;
        }

        // Get all the other types directly referenced by this type (single level depth; no recursion)
        public List<TypeInfo> GetAllTypeReferences() {
            var refs = new HashSet<TypeInfo>();

            // Constructor, event, field, method, nested type, property attributes
            var attrs = DeclaredMembers.SelectMany(m => m.CustomAttributes);
            refs.UnionWith(attrs.Select(a => a.AttributeType));

            // Events
            refs.UnionWith(DeclaredEvents.Select(e => e.EventHandlerType));

            // Fields
            refs.UnionWith(DeclaredFields.Select(f => f.FieldType));

            // Properties (return type of getters or argument type of setters)
            refs.UnionWith(DeclaredProperties.Select(p => p.PropertyType));

            // Nested types
            refs.UnionWith(DeclaredNestedTypes);
            refs.UnionWith(DeclaredNestedTypes.SelectMany(n => n.GetAllTypeReferences()));

            // Constructors
            refs.UnionWith(DeclaredConstructors.SelectMany(m => m.DeclaredParameters).Select(p => p.ParameterType));

            // Methods (includes event add/remove/raise, property get/set methods and extension methods)
            refs.UnionWith(DeclaredMethods.Select(m => m.ReturnParameter.ParameterType));
            refs.UnionWith(DeclaredMethods.SelectMany(m => m.DeclaredParameters).Select(p => p.ParameterType));

            // Method generic type parameters and constraints
            // TODO: Needs to recurse through nested generic parameters
            refs.UnionWith(DeclaredMethods.SelectMany(m => m.GenericTypeParameters ?? new List<TypeInfo>()));
            refs.UnionWith(DeclaredMethods.SelectMany(m => m.GenericTypeParameters ?? new List<TypeInfo>())
                .SelectMany(p => p.GetGenericParameterConstraints()));

            // Type declaration attributes
            refs.UnionWith(CustomAttributes.Select(a => a.AttributeType));

            // Parent type
            if (BaseType != null)
                refs.Add(BaseType);

            // Declaring type
            if (DeclaringType != null)
                refs.Add(DeclaringType);

            // Element type
            if (HasElementType)
                refs.Add(ElementType);

            // Enum type
            if (IsEnum)
                refs.Add(GetEnumUnderlyingType());

            // Generic type parameters and constraints
            // TODO: Needs to recurse through nested generic parameters
            if (GenericTypeParameters != null)
                refs.UnionWith(GenericTypeParameters);
            if (GenericTypeArguments != null)
                refs.UnionWith(GenericTypeArguments);
            refs.UnionWith(GetGenericParameterConstraints());

            // Implemented interfaces
            refs.UnionWith(ImplementedInterfaces);

            IEnumerable<TypeInfo> refList = refs.ToList();

            // Repeatedly replace arrays, pointers and references with their element types
            while (refList.Any(r => r.HasElementType))
                refList = refList.Select(r => r.HasElementType ? r.ElementType : r);

            // Remove anonymous types
            refList = refList.Where(r => !string.IsNullOrEmpty(r.FullName));

            // Eliminated named duplicates (the HashSet removes instance duplicates)
            refList = refList.GroupBy(r => r.FullName).Select(p => p.First());

            // Remove System.Object
            refList = refList.Where(r => r.FullName != "System.Object");

            return refList.ToList();
        }

        // Display name of object
        public override string Name => IsGenericParameter ? base.Name :
            (HasElementType? ElementType.Name :
                (DeclaringType != null ? DeclaringType.Name + "+" : "")
                + base.Name
                + (GenericTypeParameters != null ? "[" + string.Join(",", GenericTypeParameters.Select(x => x.Namespace != Namespace? x.FullName ?? x.Name : x.Name)) + "]" : "")
                + (GenericTypeArguments != null ? "[" + string.Join(",", GenericTypeArguments.Select(x => x.Namespace != Namespace? x.FullName ?? x.Name : x.Name)) + "]" : ""))
            + (IsArray ? "[" + new string(',', GetArrayRank() - 1) + "]" : "")
            + (IsPointer ? "*" : "");

        public string GetAccessModifierString() => this switch {
            { IsPublic: true } => "public ",
            { IsNotPublic: true } => "internal ",

            { IsNestedPublic: true } => "public ",
            { IsNestedPrivate: true } => "private ",
            { IsNestedFamily: true } => "protected ",
            { IsNestedAssembly: true } => "internal ",
            { IsNestedFamORAssem: true } => "protected internal ",
            { IsNestedFamANDAssem: true } => "private protected ",
            _ => throw new InvalidOperationException("Unknown type access modifier")
        };

        public string GetModifierString() {
            var modifiers = new StringBuilder(GetAccessModifierString());

            // An abstract sealed class is a static class
            if (IsAbstract && IsSealed)
                modifiers.Append("static ");
            else {
                if (IsAbstract && !IsInterface)
                    modifiers.Append("abstract ");
                if (IsSealed && !IsValueType && !IsEnum)
                    modifiers.Append("sealed ");
            }
            if (IsInterface)
                modifiers.Append("interface ");
            else if (IsValueType)
                modifiers.Append("struct ");
            else if (IsEnum)
                modifiers.Append("enum ");
            else
                modifiers.Append("class ");

            return modifiers.ToString();
        }

        public string GetTypeConstraintsString(Scope scope) {
            if (!IsGenericParameter)
                return string.Empty;

            var typeConstraints = GetGenericParameterConstraints();
            if (GenericParameterAttributes == GenericParameterAttributes.None && typeConstraints.Length == 0)
                return string.Empty;

            // Check if we are a nested type, and if so, exclude ourselves if we are a generic type parameter from the outer type
            // All constraints are inherited automatically by all nested types so we only have to look at the immediate outer type
            if (DeclaringMethod == null && DeclaringType.IsNested && (DeclaringType.DeclaringType.GenericTypeParameters?.Any(p => p.Name == Name) ?? false))
                return string.Empty;

            var constraintList = typeConstraints.Where(c => c.FullName != "System.ValueType").Select(c => c.GetScopedCSharpName(scope)).ToList();

            if ((GenericParameterAttributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == GenericParameterAttributes.NotNullableValueTypeConstraint)
                constraintList.Add("struct");
            if ((GenericParameterAttributes & GenericParameterAttributes.ReferenceTypeConstraint) == GenericParameterAttributes.ReferenceTypeConstraint)
                constraintList.Add("class");
            if ((GenericParameterAttributes & GenericParameterAttributes.DefaultConstructorConstraint) == GenericParameterAttributes.DefaultConstructorConstraint
                && !constraintList.Contains("struct"))
                // new() must be the last constraint specified
                constraintList.Add("new()");

            return "where " + Name + " : " + string.Join(", ", constraintList);
        }

        public override string ToString() => Name;
    }
}