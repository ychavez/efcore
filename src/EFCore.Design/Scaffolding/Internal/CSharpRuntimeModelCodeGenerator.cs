// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.VisualBasic;

namespace Microsoft.EntityFrameworkCore.Scaffolding.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class CSharpRuntimeModelCodeGenerator : ICompiledModelCodeGenerator
{
    private readonly ICSharpHelper _code;
    private readonly ICSharpRuntimeAnnotationCodeGenerator _annotationCodeGenerator;

    private const string FileExtension = ".cs";
    private const string ModelSuffix = "Model";
    private const string ModelBuilderSuffix = "ModelBuilder";
    private const string EntityTypeSuffix = "EntityType";

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public CSharpRuntimeModelCodeGenerator(
        ICSharpRuntimeAnnotationCodeGenerator annotationCodeGenerator,
        ICSharpHelper cSharpHelper)
    {
        _annotationCodeGenerator = annotationCodeGenerator;
        _code = cSharpHelper;
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual string Language
        => "C#";

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual IReadOnlyCollection<ScaffoldedFile> GenerateModel(
        IModel model,
        CompiledModelCodeGenerationOptions options)
    {
        var scaffoldedFiles = new List<ScaffoldedFile>();
        var modelCode = CreateModel(options.ModelNamespace, options.ContextType, options.UseNullableReferenceTypes);
        var modelFileName = options.ContextType.ShortDisplayName() + ModelSuffix + FileExtension;
        scaffoldedFiles.Add(new ScaffoldedFile { Path = modelFileName, Code = modelCode });

        var entityTypeIds = new Dictionary<IEntityType, (string Variable, string Class)>();
        var modelBuilderCode = CreateModelBuilder(
            model, options.ModelNamespace, options.ContextType, entityTypeIds, options.UseNullableReferenceTypes);
        var modelBuilderFileName = options.ContextType.ShortDisplayName() + ModelBuilderSuffix + FileExtension;
        scaffoldedFiles.Add(new ScaffoldedFile { Path = modelBuilderFileName, Code = modelBuilderCode });

        foreach (var (entityType, (_, @class)) in entityTypeIds)
        {
            var generatedCode = GenerateEntityType(
                entityType, options.ModelNamespace, @class, options.UseNullableReferenceTypes);

            var entityTypeFileName = @class + FileExtension;
            scaffoldedFiles.Add(new ScaffoldedFile { Path = entityTypeFileName, Code = generatedCode });
        }

        return scaffoldedFiles;
    }

    private static string GenerateHeader(SortedSet<string> namespaces, string currentNamespace, bool nullable)
    {
        for (var i = 0; i < currentNamespace.Length; i++)
        {
            if (currentNamespace[i] != '.')
            {
                continue;
            }

            namespaces.Remove(currentNamespace[..i]);
        }

        namespaces.Remove(currentNamespace);

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        foreach (var @namespace in namespaces)
        {
            builder
                .Append("using ")
                .Append(@namespace)
                .AppendLine(";");
        }

        builder.AppendLine()
            .AppendLine("#pragma warning disable 219, 612, 618");

        builder.AppendLine(nullable ? "#nullable enable" : "#nullable disable");

        builder.AppendLine();

        return builder.ToString();
    }

    private string CreateModel(
        string @namespace,
        Type contextType,
        bool nullable)
    {
        var mainBuilder = new IndentedStringBuilder();
        var namespaces = new SortedSet<string>(new NamespaceComparer())
        {
            typeof(RuntimeModel).Namespace!, typeof(DbContextAttribute).Namespace!
        };

        AddNamespace(contextType, namespaces);

        if (!string.IsNullOrEmpty(@namespace))
        {
            mainBuilder
                .Append("namespace ").AppendLine(_code.Namespace(@namespace))
                .AppendLine("{");
            mainBuilder.Indent();
        }

        var className = _code.Identifier(contextType.ShortDisplayName()) + ModelSuffix;
        mainBuilder
            .Append("[DbContext(typeof(").Append(_code.Reference(contextType)).AppendLine("))]")
            .Append("public partial class ").Append(className).AppendLine(" : " + nameof(RuntimeModel))
            .AppendLine("{");

        using (mainBuilder.Indent())
        {
            mainBuilder
                .Append("static ").Append(className).Append("()")
                .AppendLines(
                    @"
{
    var model = new "
                    + className
                    + @"();
    model.Initialize();
    model.Customize();
    _instance = model;
}")
                .AppendLine()
                .Append("private static ").Append(className).AppendLine(" _instance;")
                .AppendLine("public static IModel Instance => _instance;")
                .AppendLine()
                .AppendLine("partial void Initialize();")
                .AppendLine()
                .AppendLine("partial void Customize();");
        }

        mainBuilder.AppendLine("}");

        if (!string.IsNullOrEmpty(@namespace))
        {
            mainBuilder.DecrementIndent();
            mainBuilder.AppendLine("}");
        }

        return GenerateHeader(namespaces, @namespace, nullable) + mainBuilder;
    }

    private string CreateModelBuilder(
        IModel model,
        string @namespace,
        Type contextType,
        Dictionary<IEntityType, (string Variable, string Class)> entityTypeIds,
        bool nullable)
    {
        var mainBuilder = new IndentedStringBuilder();
        var methodBuilder = new IndentedStringBuilder();
        var namespaces = new SortedSet<string>(new NamespaceComparer())
        {
            typeof(RuntimeModel).Namespace!, typeof(DbContextAttribute).Namespace!
        };

        if (!string.IsNullOrEmpty(@namespace))
        {
            mainBuilder
                .Append("namespace ").AppendLine(_code.Namespace(@namespace))
                .AppendLine("{");
            mainBuilder.Indent();
        }

        var className = _code.Identifier(contextType.ShortDisplayName()) + ModelSuffix;
        mainBuilder
            .Append("public partial class ").AppendLine(className)
            .AppendLine("{");

        using (mainBuilder.Indent())
        {
            mainBuilder
                .AppendLine("partial void Initialize()")
                .AppendLine("{");
            using (mainBuilder.Indent())
            {
                var entityTypes = model.GetEntityTypesInHierarchicalOrder();
                var variables = new HashSet<string>();

                var anyEntityTypes = false;
                foreach (var entityType in entityTypes)
                {
                    anyEntityTypes = true;
                    var variableName = _code.Identifier(entityType.ShortName(), variables, capitalize: false);

                    var firstChar = variableName[0] == '@' ? variableName[1] : variableName[0];
                    var entityClassName = firstChar == '_'
                        ? EntityTypeSuffix + variableName[1..]
                        : char.ToUpperInvariant(firstChar) + variableName[(variableName[0] == '@' ? 2 : 1)..] + EntityTypeSuffix;

                    entityTypeIds[entityType] = (variableName, entityClassName);

                    mainBuilder
                        .Append("var ")
                        .Append(variableName)
                        .Append(" = ")
                        .Append(entityClassName)
                        .Append(".Create(this");

                    if (entityType.BaseType != null)
                    {
                        mainBuilder
                            .Append(", ")
                            .Append(entityTypeIds[entityType.BaseType].Variable);
                    }

                    mainBuilder
                        .AppendLine(");");
                }

                if (anyEntityTypes)
                {
                    mainBuilder.AppendLine();
                }

                var anyForeignKeys = false;
                foreach (var (entityType, namePair) in entityTypeIds)
                {
                    var foreignKeyNumber = 1;
                    var (variableName, entityClassName) = namePair;
                    foreach (var foreignKey in entityType.GetDeclaredForeignKeys())
                    {
                        anyForeignKeys = true;
                        var principalVariable = entityTypeIds[foreignKey.PrincipalEntityType].Variable;

                        mainBuilder
                            .Append(entityClassName)
                            .Append(".CreateForeignKey")
                            .Append(foreignKeyNumber++.ToString())
                            .Append("(")
                            .Append(variableName)
                            .Append(", ")
                            .Append(principalVariable)
                            .AppendLine(");");
                    }
                }

                if (anyForeignKeys)
                {
                    mainBuilder.AppendLine();
                }

                var anySkipNavigations = false;
                foreach (var (entityType, namePair) in entityTypeIds)
                {
                    var navigationNumber = 1;
                    var (variableName, entityClassName) = namePair;
                    foreach (var navigation in entityType.GetDeclaredSkipNavigations())
                    {
                        anySkipNavigations = true;
                        var targetVariable = entityTypeIds[navigation.TargetEntityType].Variable;
                        var joinVariable = entityTypeIds[navigation.JoinEntityType].Variable;

                        mainBuilder
                            .Append(entityClassName)
                            .Append(".CreateSkipNavigation")
                            .Append(navigationNumber++.ToString())
                            .Append("(")
                            .Append(variableName)
                            .Append(", ")
                            .Append(targetVariable)
                            .Append(", ")
                            .Append(joinVariable)
                            .AppendLine(");");
                    }
                }

                if (anySkipNavigations)
                {
                    mainBuilder.AppendLine();
                }

                foreach (var (_, namePair) in entityTypeIds)
                {
                    var (variableName, entityClassName) = namePair;

                    mainBuilder
                        .Append(entityClassName)
                        .Append(".CreateAnnotations")
                        .Append("(")
                        .Append(variableName)
                        .AppendLine(");");
                }

                if (anyEntityTypes)
                {
                    mainBuilder.AppendLine();
                }

                var parameters = new CSharpRuntimeAnnotationCodeGeneratorParameters(
                    "this",
                    className,
                    mainBuilder,
                    methodBuilder,
                    namespaces,
                    variables);

                foreach (var typeConfiguration in model.GetTypeMappingConfigurations())
                {
                    Create(typeConfiguration, parameters);
                }

                CreateAnnotations(model, _annotationCodeGenerator.Generate, parameters);
            }

            mainBuilder
                .AppendLine("}");

            var methods = methodBuilder.ToString();
            if (!string.IsNullOrEmpty(methods))
            {
                mainBuilder.AppendLine()
                    .AppendLines(methods);
            }
        }

        mainBuilder.AppendLine("}");

        if (!string.IsNullOrEmpty(@namespace))
        {
            mainBuilder.DecrementIndent();
            mainBuilder.AppendLine("}");
        }

        return GenerateHeader(namespaces, @namespace, nullable) + mainBuilder;
    }

    private void Create(
        ITypeMappingConfiguration typeConfiguration,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        var variableName = _code.Identifier("type", parameters.ScopeVariables, capitalize: false);

        var mainBuilder = parameters.MainBuilder;
        mainBuilder
            .Append("var ").Append(variableName).Append(" = ").Append(parameters.TargetName).AppendLine(".AddTypeMappingConfiguration(")
            .IncrementIndent()
            .Append(_code.Literal(typeConfiguration.ClrType));

        AddNamespace(typeConfiguration.ClrType, parameters.Namespaces);

        if (typeConfiguration.GetMaxLength() != null)
        {
            mainBuilder.AppendLine(",")
                .Append("maxLength: ")
                .Append(_code.Literal(typeConfiguration.GetMaxLength()));
        }

        if (typeConfiguration.IsUnicode() != null)
        {
            mainBuilder.AppendLine(",")
                .Append("unicode: ")
                .Append(_code.Literal(typeConfiguration.IsUnicode()));
        }

        if (typeConfiguration.GetPrecision() != null)
        {
            mainBuilder.AppendLine(",")
                .Append("precision: ")
                .Append(_code.Literal(typeConfiguration.GetPrecision()));
        }

        if (typeConfiguration.GetScale() != null)
        {
            mainBuilder.AppendLine(",")
                .Append("scale: ")
                .Append(_code.Literal(typeConfiguration.GetScale()));
        }

        var providerClrType = typeConfiguration.GetProviderClrType();
        if (providerClrType != null)
        {
            AddNamespace(providerClrType, parameters.Namespaces);

            mainBuilder.AppendLine(",")
                .Append("providerPropertyType: ")
                .Append(_code.Literal(providerClrType));
        }

        var valueConverterType = (Type?)typeConfiguration[CoreAnnotationNames.ValueConverterType];
        if (valueConverterType != null)
        {
            AddNamespace(valueConverterType, parameters.Namespaces);

            mainBuilder.AppendLine(",")
                .Append("valueConverter: new ")
                .Append(_code.Reference(valueConverterType))
                .Append("()");
        }

        mainBuilder
            .AppendLine(");")
            .DecrementIndent();

        CreateAnnotations(
            typeConfiguration,
            _annotationCodeGenerator.Generate,
            parameters with { TargetName = variableName });

        mainBuilder.AppendLine();
    }

    private string GenerateEntityType(IEntityType entityType, string @namespace, string className, bool nullable)
    {
        var mainBuilder = new IndentedStringBuilder();
        var methodBuilder = new IndentedStringBuilder();
        var namespaces = new SortedSet<string>(new NamespaceComparer())
        {
            typeof(BindingFlags).Namespace!, typeof(RuntimeEntityType).Namespace!
        };

        if (!string.IsNullOrEmpty(@namespace))
        {
            mainBuilder
                .Append("namespace ").AppendLine(_code.Namespace(@namespace))
                .AppendLine("{");
            mainBuilder.Indent();
        }

        mainBuilder
            .Append("internal partial class ").AppendLine(className)
            .AppendLine("{");
        using (mainBuilder.Indent())
        {
            CreateEntityType(entityType, mainBuilder, methodBuilder, namespaces, className, nullable);

            var foreignKeyNumber = 1;
            foreach (var foreignKey in entityType.GetDeclaredForeignKeys())
            {
                CreateForeignKey(foreignKey, foreignKeyNumber++, mainBuilder, methodBuilder, namespaces, className, nullable);
            }

            var navigationNumber = 1;
            foreach (var navigation in entityType.GetDeclaredSkipNavigations())
            {
                CreateSkipNavigation(navigation, navigationNumber++, mainBuilder, methodBuilder, namespaces, className, nullable);
            }

            CreateAnnotations(entityType, mainBuilder, methodBuilder, namespaces, className);
        }

        mainBuilder.AppendLine("}");

        if (!string.IsNullOrEmpty(@namespace))
        {
            mainBuilder.DecrementIndent();
            mainBuilder.AppendLine("}");
        }

        return GenerateHeader(namespaces, @namespace, nullable) + mainBuilder + methodBuilder;
    }

    private void CreateEntityType(
        IEntityType entityType,
        IndentedStringBuilder mainBuilder,
        IndentedStringBuilder methodBuilder,
        SortedSet<string> namespaces,
        string className,
        bool nullable)
    {
        mainBuilder
            .Append("public static RuntimeEntityType Create")
            .Append("(RuntimeModel model, RuntimeEntityType");

        if (nullable)
        {
            mainBuilder
                .Append("?");
        }

        mainBuilder.AppendLine(" baseEntityType = null)")
            .AppendLine("{");

        using (mainBuilder.Indent())
        {
            const string entityTypeVariable = "runtimeEntityType";
            var variables = new HashSet<string>
            {
                "model",
                "baseEntityType",
                entityTypeVariable
            };

            var parameters = new CSharpRuntimeAnnotationCodeGeneratorParameters(
                entityTypeVariable,
                className,
                mainBuilder,
                methodBuilder,
                namespaces,
                variables);

            Create(entityType, parameters);

            var propertyVariables = new Dictionary<IProperty, string>();
            foreach (var property in entityType.GetDeclaredProperties())
            {
                Create(property, propertyVariables, parameters);
            }

            foreach (var property in entityType.GetDeclaredServiceProperties())
            {
                Create(property, parameters);
            }

            foreach (var key in entityType.GetDeclaredKeys())
            {
                Create(key, propertyVariables, parameters, nullable);
            }

            foreach (var index in entityType.GetDeclaredIndexes())
            {
                Create(index, propertyVariables, parameters, nullable);
            }

            foreach (var trigger in entityType.GetDeclaredTriggers())
            {
                Create(trigger, parameters);
            }

            mainBuilder
                .Append("return ")
                .Append(entityTypeVariable)
                .AppendLine(";");
        }

        mainBuilder
            .AppendLine("}");
    }

    private void Create(IEntityType entityType, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        var runtimeEntityType = entityType as IRuntimeEntityType;
        if ((entityType.ConstructorBinding is not null
                && ((runtimeEntityType?.GetConstructorBindingConfigurationSource()).OverridesStrictly(ConfigurationSource.Convention)
                    || entityType.ConstructorBinding is FactoryMethodBinding))
            || (runtimeEntityType?.ServiceOnlyConstructorBinding is not null
                && (runtimeEntityType.GetServiceOnlyConstructorBindingConfigurationSource()
                        .OverridesStrictly(ConfigurationSource.Convention)
                    || runtimeEntityType.ServiceOnlyConstructorBinding is FactoryMethodBinding)))
        {
            throw new InvalidOperationException(
                DesignStrings.CompiledModelConstructorBinding(
                    entityType.ShortName(), "Customize()", parameters.ClassName));
        }

        if (entityType.GetQueryFilter() != null)
        {
            throw new InvalidOperationException(DesignStrings.CompiledModelQueryFilter(entityType.ShortName()));
        }

#pragma warning disable CS0618 // Type or member is obsolete
        if (entityType.GetDefiningQuery() != null)
        {
            // TODO: Move to InMemoryCSharpRuntimeAnnotationCodeGenerator, see #21624
            throw new InvalidOperationException(DesignStrings.CompiledModelDefiningQuery(entityType.ShortName()));
        }
#pragma warning restore CS0618 // Type or member is obsolete

        AddNamespace(entityType.ClrType, parameters.Namespaces);

        var mainBuilder = parameters.MainBuilder;
        mainBuilder
            .Append("var ")
            .Append(parameters.TargetName)
            .AppendLine(" = model.AddEntityType(")
            .IncrementIndent()
            .Append(_code.Literal(entityType.Name)).AppendLine(",")
            .Append(_code.Literal(entityType.ClrType)).AppendLine(",")
            .Append("baseEntityType");

        if (entityType.HasSharedClrType)
        {
            mainBuilder.AppendLine(",")
                .Append("sharedClrType: ")
                .Append(_code.Literal(entityType.HasSharedClrType));
        }

        var discriminatorProperty = entityType.GetDiscriminatorPropertyName();
        if (discriminatorProperty != null)
        {
            mainBuilder.AppendLine(",")
                .Append("discriminatorProperty: ")
                .Append(_code.Literal(discriminatorProperty));
        }

        var changeTrackingStrategy = entityType.GetChangeTrackingStrategy();
        if (changeTrackingStrategy != ChangeTrackingStrategy.Snapshot)
        {
            parameters.Namespaces.Add(typeof(ChangeTrackingStrategy).Namespace!);

            mainBuilder.AppendLine(",")
                .Append("changeTrackingStrategy: ")
                .Append(_code.Literal(changeTrackingStrategy));
        }

        var indexerPropertyInfo = entityType.FindIndexerPropertyInfo();
        if (indexerPropertyInfo != null)
        {
            mainBuilder.AppendLine(",")
                .Append("indexerPropertyInfo: RuntimeEntityType.FindIndexerProperty(")
                .Append(_code.Literal(entityType.ClrType))
                .Append(")");
        }

        if (entityType.IsPropertyBag)
        {
            mainBuilder.AppendLine(",")
                .Append("propertyBag: ")
                .Append(_code.Literal(true));
        }

        var discriminatorValue = entityType.GetDiscriminatorValue();
        if (discriminatorValue != null)
        {
            AddNamespace(discriminatorValue.GetType(), parameters.Namespaces);

            mainBuilder.AppendLine(",")
                .Append("discriminatorValue: ")
                .Append(_code.UnknownLiteral(discriminatorValue));
        }

        mainBuilder
            .AppendLine(");")
            .AppendLine()
            .DecrementIndent();
    }

    private void Create(
        IProperty property,
        Dictionary<IProperty, string> propertyVariables,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        var valueGeneratorFactoryType = (Type?)property[CoreAnnotationNames.ValueGeneratorFactoryType];
        if (valueGeneratorFactoryType == null
            && property.GetValueGeneratorFactory() != null)
        {
            throw new InvalidOperationException(
                DesignStrings.CompiledModelValueGenerator(
                    property.DeclaringEntityType.ShortName(), property.Name, nameof(PropertyBuilder.HasValueGeneratorFactory)));
        }

        var valueComparerType = (Type?)property[CoreAnnotationNames.ValueComparerType];
        if (valueComparerType == null
            && property[CoreAnnotationNames.ValueComparer] != null)
        {
            throw new InvalidOperationException(
                DesignStrings.CompiledModelValueComparer(
                    property.DeclaringEntityType.ShortName(), property.Name, nameof(PropertyBuilder.HasConversion)));
        }

        var providerValueComparerType = (Type?)property[CoreAnnotationNames.ProviderValueComparerType];
        if (providerValueComparerType == null
            && property[CoreAnnotationNames.ProviderValueComparer] != null)
        {
            throw new InvalidOperationException(
                DesignStrings.CompiledModelValueComparer(
                    property.DeclaringEntityType.ShortName(), property.Name, nameof(PropertyBuilder.HasConversion)));
        }

        var valueConverterType = (Type?)property[CoreAnnotationNames.ValueConverterType];
        if (valueConverterType == null
            && property.GetValueConverter() != null)
        {
            throw new InvalidOperationException(
                DesignStrings.CompiledModelValueConverter(
                    property.DeclaringEntityType.ShortName(), property.Name, nameof(PropertyBuilder.HasConversion)));
        }

        if (property is IConventionProperty conventionProperty
            && conventionProperty.GetTypeMappingConfigurationSource() != null)
        {
            throw new InvalidOperationException(
                DesignStrings.CompiledModelTypeMapping(
                    property.DeclaringEntityType.ShortName(), property.Name, "Customize()", parameters.ClassName));
        }

        var mainBuilder = parameters.MainBuilder;
        string? valueComparerString = null;
        if (valueComparerType == null)
        {
            var valueComparer = property.GetValueComparer();
            valueComparerType = valueComparer.GetType();
            if (valueComparerType.IsGenericType
                && valueComparerType.GetGenericTypeDefinition() == typeof(ValueComparer<>))
            {
                AddNamespace(valueComparerType, parameters.Namespaces);

                var expressionVariables = new HashSet<string>();

                valueComparerString = $"new {_code.Reference(valueComparerType)}"
                    + $"({CreateExpression(valueComparer.EqualsExpression, mainBuilder.IndentCount, expressionVariables, parameters.Namespaces)}, "
                    + $"{CreateExpression(valueComparer.HashCodeExpression, mainBuilder.IndentCount, expressionVariables, parameters.Namespaces)}, "
                    + $"{CreateExpression(valueComparer.SnapshotExpression, mainBuilder.IndentCount, expressionVariables, parameters.Namespaces)})";

                if (expressionVariables.Count > 0)
                {
                    var variableBuilder = new IndentedStringBuilder();
                    variableBuilder.IncrementIndent(mainBuilder.IndentCount);
                    // TODO: print out the expression variables
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unexpected comparer {valueComparerType.DisplayName()} for {property.DeclaringEntityType.DisplayName()}.{property.Name}");
            }
        }

        var variableName = _code.Identifier(property.Name, parameters.ScopeVariables, capitalize: false);
        propertyVariables[property] = variableName;

        mainBuilder
            .Append("var ").Append(variableName).Append(" = ").Append(parameters.TargetName).AppendLine(".AddProperty(")
            .IncrementIndent()
            .Append(_code.Literal(property.Name));

        PropertyBaseParameters(property, parameters);

        if (property.IsNullable)
        {
            mainBuilder.AppendLine(",")
                .Append("nullable: ")
                .Append(_code.Literal(true));
        }

        if (property.IsConcurrencyToken)
        {
            mainBuilder.AppendLine(",")
                .Append("concurrencyToken: ")
                .Append(_code.Literal(true));
        }

        if (property.ValueGenerated != ValueGenerated.Never)
        {
            mainBuilder.AppendLine(",")
                .Append("valueGenerated: ")
                .Append(_code.Literal(property.ValueGenerated));
        }

        if (property.GetBeforeSaveBehavior() != PropertySaveBehavior.Save)
        {
            mainBuilder.AppendLine(",")
                .Append("beforeSaveBehavior: ")
                .Append(_code.Literal(property.GetBeforeSaveBehavior()));
        }

        if (property.GetAfterSaveBehavior() != PropertySaveBehavior.Save)
        {
            mainBuilder.AppendLine(",")
                .Append("afterSaveBehavior: ")
                .Append(_code.Literal(property.GetAfterSaveBehavior()));
        }

        if (property.GetMaxLength() != null)
        {
            mainBuilder.AppendLine(",")
                .Append("maxLength: ")
                .Append(_code.Literal(property.GetMaxLength()));
        }

        if (property.IsUnicode() != null)
        {
            mainBuilder.AppendLine(",")
                .Append("unicode: ")
                .Append(_code.Literal(property.IsUnicode()));
        }

        if (property.GetPrecision() != null)
        {
            mainBuilder.AppendLine(",")
                .Append("precision: ")
                .Append(_code.Literal(property.GetPrecision()));
        }

        if (property.GetScale() != null)
        {
            mainBuilder.AppendLine(",")
                .Append("scale: ")
                .Append(_code.Literal(property.GetScale()));
        }

        var providerClrType = property.GetProviderClrType();
        if (providerClrType != null)
        {
            AddNamespace(providerClrType, parameters.Namespaces);

            mainBuilder.AppendLine(",")
                .Append("providerPropertyType: ")
                .Append(_code.Literal(providerClrType));
        }

        if (valueGeneratorFactoryType != null)
        {
            AddNamespace(valueGeneratorFactoryType, parameters.Namespaces);

            mainBuilder.AppendLine(",")
                .Append("valueGeneratorFactory: new ")
                .Append(_code.Reference(valueGeneratorFactoryType))
                .Append("().Create");
        }

        if (valueConverterType != null)
        {
            AddNamespace(valueConverterType, parameters.Namespaces);

            mainBuilder.AppendLine(",")
                .Append("valueConverter: new ")
                .Append(_code.Reference(valueConverterType))
                .Append("()");
        }

        if (valueComparerString != null)
        {
            var valueComparer = property.GetValueComparer();
            valueComparerType = valueComparer.GetType();
            if (valueComparerType.IsGenericType
                && valueComparerType.GetGenericTypeDefinition() == typeof(ValueComparer<>))
            {
                AddNamespace(valueComparerType, parameters.Namespaces);
                var equalsBuilder = new IndentedStringBuilder();
                equalsBuilder.IncrementIndent(mainBuilder.IndentCount);

                mainBuilder.AppendLine(",")
                    .Append("valueComparer: ")
                    .Append(valueComparerString);
                //TODO: also set keyValueComparer
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unexpected comparer {valueComparerType.DisplayName()} for {property.DeclaringEntityType.DisplayName()}.{property.Name}");
            }
        }
        else if(valueComparerType != null)
        {
            AddNamespace(valueComparerType, parameters.Namespaces);

            mainBuilder.AppendLine(",")
                .Append("valueComparer: new ")
                .Append(_code.Reference(valueComparerType))
                .Append("()");
        }

        if (providerValueComparerType != null)
        {
            AddNamespace(providerValueComparerType, parameters.Namespaces);

            mainBuilder.AppendLine(",")
                .Append("providerValueComparer: new ")
                .Append(_code.Reference(providerValueComparerType))
                .Append("()");
        }

        mainBuilder
            .AppendLine(");")
            .DecrementIndent();

        CreateAnnotations(
            property,
            _annotationCodeGenerator.Generate,
            parameters with { TargetName = variableName });

        mainBuilder.AppendLine();
    }

    private void PropertyBaseParameters(
        IPropertyBase property,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters,
        bool skipType = false)
    {
        var mainBuilder = parameters.MainBuilder;

        if (!skipType)
        {
            AddNamespace(property.ClrType, parameters.Namespaces);
            mainBuilder.AppendLine(",")
                .Append(_code.Literal(property.ClrType));
        }

        var propertyInfo = property.PropertyInfo;
        if (propertyInfo != null)
        {
            AddNamespace(propertyInfo.DeclaringType!, parameters.Namespaces);

            mainBuilder.AppendLine(",")
                .Append("propertyInfo: ");

            if (property.IsIndexerProperty())
            {
                mainBuilder
                    .Append(parameters.TargetName)
                    .Append(".FindIndexerPropertyInfo()");
            }
            else
            {
                mainBuilder
                    .Append(_code.Literal(propertyInfo.DeclaringType!))
                    .Append(".GetProperty(")
                    .Append(_code.Literal(propertyInfo.Name))
                    .Append(", ")
                    .Append(propertyInfo.GetAccessors().Any() ? "BindingFlags.Public" : "BindingFlags.NonPublic")
                    .Append(propertyInfo.IsStatic() ? " | BindingFlags.Static" : " | BindingFlags.Instance")
                    .Append(" | BindingFlags.DeclaredOnly)");
            }
        }

        var fieldInfo = property.FieldInfo;
        if (fieldInfo != null)
        {
            AddNamespace(fieldInfo.DeclaringType!, parameters.Namespaces);

            mainBuilder.AppendLine(",")
                .Append("fieldInfo: ")
                .Append(_code.Literal(fieldInfo.DeclaringType!))
                .Append(".GetField(")
                .Append(_code.Literal(fieldInfo.Name))
                .Append(", ")
                .Append(fieldInfo.IsPublic ? "BindingFlags.Public" : "BindingFlags.NonPublic")
                .Append(fieldInfo.IsStatic ? " | BindingFlags.Static" : " | BindingFlags.Instance")
                .Append(" | BindingFlags.DeclaredOnly)");
        }

        var propertyAccessMode = property.GetPropertyAccessMode();
        if (propertyAccessMode != Model.DefaultPropertyAccessMode)
        {
            parameters.Namespaces.Add(typeof(PropertyAccessMode).Namespace!);

            mainBuilder.AppendLine(",")
                .Append("propertyAccessMode: ")
                .Append(_code.Literal(propertyAccessMode));
        }
    }

    private void FindProperties(
        string entityTypeVariable,
        IEnumerable<IProperty> properties,
        IndentedStringBuilder mainBuilder,
        bool nullable,
        Dictionary<IProperty, string>? propertyVariables = null)
    {
        mainBuilder.Append("new[] { ");
        var first = true;
        foreach (var property in properties)
        {
            if (first)
            {
                first = false;
            }
            else
            {
                mainBuilder.Append(", ");
            }

            if (propertyVariables != null
                && propertyVariables.TryGetValue(property, out var propertyVariable))
            {
                mainBuilder.Append(propertyVariable);
            }
            else
            {
                mainBuilder
                    .Append(entityTypeVariable)
                    .Append(".FindProperty(")
                    .Append(_code.Literal(property.Name))
                    .Append(")");

                if (nullable)
                {
                    mainBuilder
                        .Append("!");
                }
            }
        }

        mainBuilder.Append(" }");
    }

    private void Create(
        IServiceProperty property,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        var variableName = _code.Identifier(property.Name, parameters.ScopeVariables, capitalize: false);

        var mainBuilder = parameters.MainBuilder;
        mainBuilder
            .Append("var ").Append(variableName).Append(" = ").Append(parameters.TargetName).AppendLine(".AddServiceProperty(")
            .IncrementIndent()
            .Append(_code.Literal(property.Name));

        PropertyBaseParameters(property, parameters, skipType: true);

        mainBuilder
            .AppendLine(");")
            .DecrementIndent();

        CreateAnnotations(
            property,
            _annotationCodeGenerator.Generate,
            parameters with { TargetName = variableName });

        mainBuilder.AppendLine();
    }

    private void Create(
        IKey key,
        Dictionary<IProperty, string> propertyVariables,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters,
        bool nullable)
    {
        var variableName = _code.Identifier("key", parameters.ScopeVariables);

        var mainBuilder = parameters.MainBuilder;
        mainBuilder
            .Append("var ").Append(variableName).Append(" = ").Append(parameters.TargetName).AppendLine(".AddKey(")
            .IncrementIndent();
        FindProperties(parameters.TargetName, key.Properties, mainBuilder, nullable, propertyVariables);
        mainBuilder
            .AppendLine(");")
            .DecrementIndent();

        if (key.IsPrimaryKey())
        {
            mainBuilder
                .Append(parameters.TargetName)
                .Append(".SetPrimaryKey(")
                .Append(variableName)
                .AppendLine(");");
        }

        CreateAnnotations(
            key,
            _annotationCodeGenerator.Generate,
            parameters with { TargetName = variableName });

        mainBuilder.AppendLine();
    }

    private void Create(
        IIndex index,
        Dictionary<IProperty, string> propertyVariables,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters,
        bool nullable)
    {
        var variableName = _code.Identifier(index.Name ?? "index", parameters.ScopeVariables, capitalize: false);

        var mainBuilder = parameters.MainBuilder;
        mainBuilder
            .Append("var ").Append(variableName).Append(" = ").Append(parameters.TargetName).AppendLine(".AddIndex(")
            .IncrementIndent();

        FindProperties(parameters.TargetName, index.Properties, mainBuilder, nullable, propertyVariables);

        if (index.Name != null)
        {
            mainBuilder.AppendLine(",")
                .Append("name: ")
                .Append(_code.Literal(index.Name));
        }

        if (index.IsUnique)
        {
            mainBuilder.AppendLine(",")
                .Append("unique: ")
                .Append(_code.Literal(true));
        }

        mainBuilder
            .AppendLine(");")
            .DecrementIndent();

        CreateAnnotations(
            index,
            _annotationCodeGenerator.Generate,
            parameters with { TargetName = variableName });

        mainBuilder.AppendLine();
    }

    private void CreateForeignKey(
        IForeignKey foreignKey,
        int foreignKeyNumber,
        IndentedStringBuilder mainBuilder,
        IndentedStringBuilder methodBuilder,
        SortedSet<string> namespaces,
        string className,
        bool nullable)
    {
        const string declaringEntityType = "declaringEntityType";
        const string principalEntityType = "principalEntityType";
        mainBuilder.AppendLine()
            .Append("public static RuntimeForeignKey CreateForeignKey").Append(foreignKeyNumber.ToString())
            .Append("(RuntimeEntityType ").Append(declaringEntityType)
            .Append(", RuntimeEntityType ").Append(principalEntityType).AppendLine(")")
            .AppendLine("{");

        using (mainBuilder.Indent())
        {
            const string foreignKeyVariable = "runtimeForeignKey";
            var variables = new HashSet<string>
            {
                declaringEntityType,
                principalEntityType,
                foreignKeyVariable
            };

            mainBuilder
                .Append("var ").Append(foreignKeyVariable).Append(" = ")
                .Append(declaringEntityType).Append(".AddForeignKey(").IncrementIndent();
            FindProperties(declaringEntityType, foreignKey.Properties, mainBuilder, nullable);

            mainBuilder.AppendLine(",")
                .Append(principalEntityType).Append(".FindKey(");
            FindProperties(principalEntityType, foreignKey.PrincipalKey.Properties, mainBuilder, nullable);
            mainBuilder.Append(")");
            if (nullable)
            {
                mainBuilder.Append("!");
            }

            mainBuilder.AppendLine(",")
                .Append(principalEntityType);

            if (foreignKey.DeleteBehavior != ForeignKey.DefaultDeleteBehavior)
            {
                namespaces.Add(typeof(DeleteBehavior).Namespace!);

                mainBuilder.AppendLine(",")
                    .Append("deleteBehavior: ")
                    .Append(_code.Literal(foreignKey.DeleteBehavior));
            }

            if (foreignKey.IsUnique)
            {
                mainBuilder.AppendLine(",")
                    .Append("unique: ")
                    .Append(_code.Literal(true));
            }

            if (foreignKey.IsRequired)
            {
                mainBuilder.AppendLine(",")
                    .Append("required: ")
                    .Append(_code.Literal(true));
            }

            if (foreignKey.IsRequiredDependent)
            {
                mainBuilder.AppendLine(",")
                    .Append("requiredDependent: ")
                    .Append(_code.Literal(true));
            }

            if (foreignKey.IsOwnership)
            {
                mainBuilder.AppendLine(",")
                    .Append("ownership: ")
                    .Append(_code.Literal(true));
            }

            mainBuilder
                .AppendLine(");")
                .AppendLine()
                .DecrementIndent();

            var parameters = new CSharpRuntimeAnnotationCodeGeneratorParameters(
                foreignKeyVariable,
                className,
                mainBuilder,
                methodBuilder,
                namespaces,
                variables);

            var navigation = foreignKey.DependentToPrincipal;
            if (navigation != null)
            {
                Create(navigation, foreignKeyVariable, parameters with { TargetName = declaringEntityType });
            }

            navigation = foreignKey.PrincipalToDependent;
            if (navigation != null)
            {
                Create(navigation, foreignKeyVariable, parameters with { TargetName = principalEntityType });
            }

            CreateAnnotations(
                foreignKey,
                _annotationCodeGenerator.Generate,
                parameters);

            mainBuilder
                .Append("return ")
                .Append(foreignKeyVariable)
                .AppendLine(";");
        }

        mainBuilder
            .AppendLine("}");
    }

    private void Create(
        INavigation navigation,
        string foreignKeyVariable,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        var mainBuilder = parameters.MainBuilder;
        var navigationVariable = _code.Identifier(navigation.Name, parameters.ScopeVariables, capitalize: false);
        mainBuilder
            .Append("var ").Append(navigationVariable).Append(" = ")
            .Append(parameters.TargetName).Append(".AddNavigation(").IncrementIndent()
            .Append(_code.Literal(navigation.Name)).AppendLine(",")
            .Append(foreignKeyVariable).AppendLine(",")
            .Append("onDependent: ").Append(_code.Literal(navigation.IsOnDependent));

        PropertyBaseParameters(navigation, parameters);

        if (navigation.IsEagerLoaded)
        {
            mainBuilder.AppendLine(",")
                .Append("eagerLoaded: ").Append(_code.Literal(true));
        }

        mainBuilder
            .AppendLine(");")
            .AppendLine()
            .DecrementIndent();

        CreateAnnotations(
            navigation,
            _annotationCodeGenerator.Generate,
            parameters with { TargetName = navigationVariable });
    }

    private void CreateSkipNavigation(
        ISkipNavigation navigation,
        int navigationNumber,
        IndentedStringBuilder mainBuilder,
        IndentedStringBuilder methodBuilder,
        SortedSet<string> namespaces,
        string className,
        bool nullable)
    {
        const string declaringEntityType = "declaringEntityType";
        const string targetEntityType = "targetEntityType";
        const string joinEntityType = "joinEntityType";
        mainBuilder.AppendLine()
            .Append("public static RuntimeSkipNavigation CreateSkipNavigation")
            .Append(navigationNumber.ToString())
            .Append("(RuntimeEntityType ").Append(declaringEntityType)
            .Append(", RuntimeEntityType ").Append(targetEntityType)
            .Append(", RuntimeEntityType ").Append(joinEntityType).AppendLine(")")
            .AppendLine("{");

        using (mainBuilder.Indent())
        {
            const string navigationVariable = "skipNavigation";
            var variables = new HashSet<string>
            {
                declaringEntityType,
                targetEntityType,
                joinEntityType,
                navigationVariable
            };

            var parameters = new CSharpRuntimeAnnotationCodeGeneratorParameters(
                navigationVariable,
                className,
                mainBuilder,
                methodBuilder,
                namespaces,
                variables);

            mainBuilder
                .Append("var ").Append(navigationVariable).Append(" = ")
                .Append(declaringEntityType).AppendLine(".AddSkipNavigation(").IncrementIndent()
                .Append(_code.Literal(navigation.Name)).AppendLine(",")
                .Append(targetEntityType).AppendLine(",")
                .Append(joinEntityType).AppendLine(".FindForeignKey(");
            using (mainBuilder.Indent())
            {
                FindProperties(joinEntityType, navigation.ForeignKey.Properties, mainBuilder, nullable);
                mainBuilder.AppendLine(",")
                    .Append(declaringEntityType).Append(".FindKey(");
                FindProperties(declaringEntityType, navigation.ForeignKey.PrincipalKey.Properties, mainBuilder, nullable);
                mainBuilder.Append(")");
                if (nullable)
                {
                    mainBuilder.Append("!");
                }

                mainBuilder.AppendLine(",")
                    .Append(declaringEntityType).Append(")");
                if (nullable)
                {
                    mainBuilder.Append("!");
                }
            }

            mainBuilder.AppendLine(",")
                .Append(_code.Literal(navigation.IsCollection)).AppendLine(",")
                .Append(_code.Literal(navigation.IsOnDependent));

            PropertyBaseParameters(navigation, parameters with { TargetName = declaringEntityType });

            if (navigation.IsEagerLoaded)
            {
                mainBuilder.AppendLine(",")
                    .Append("eagerLoaded: ").Append(_code.Literal(true));
            }

            mainBuilder
                .AppendLine(");")
                .DecrementIndent();

            mainBuilder.AppendLine();

            variables.Add("inverse");
            mainBuilder
                .Append("var inverse = ").Append(targetEntityType).Append(".FindSkipNavigation(")
                .Append(_code.Literal(navigation.Inverse.Name)).AppendLine(");")
                .AppendLine("if (inverse != null)")
                .AppendLine("{");
            using (mainBuilder.Indent())
            {
                mainBuilder
                    .Append(navigationVariable).AppendLine(".Inverse = inverse;")
                    .Append("inverse.Inverse = ").Append(navigationVariable).AppendLine(";");
            }

            mainBuilder
                .AppendLine("}")
                .AppendLine();

            CreateAnnotations(
                navigation,
                _annotationCodeGenerator.Generate,
                parameters);

            mainBuilder
                .Append("return ")
                .Append(navigationVariable)
                .AppendLine(";");
        }

        mainBuilder
            .AppendLine("}");
    }

    private void Create(ITrigger trigger, CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        var triggerVariable = _code.Identifier(trigger.ModelName, parameters.ScopeVariables, capitalize: false);

        var mainBuilder = parameters.MainBuilder;
        mainBuilder
            .Append("var ").Append(triggerVariable).Append(" = ").Append(parameters.TargetName).AppendLine(".AddTrigger(")
            .IncrementIndent()
            .Append(_code.Literal(trigger.ModelName))
            .AppendLine(");")
            .DecrementIndent();

        CreateAnnotations(
            trigger,
            _annotationCodeGenerator.Generate,
            parameters with { TargetName = triggerVariable });

        mainBuilder.AppendLine();
    }

    private void CreateAnnotations(
        IEntityType entityType,
        IndentedStringBuilder mainBuilder,
        IndentedStringBuilder methodBuilder,
        SortedSet<string> namespaces,
        string className)
    {
        mainBuilder.AppendLine()
            .Append("public static void CreateAnnotations")
            .AppendLine("(RuntimeEntityType runtimeEntityType)")
            .AppendLine("{");

        using (mainBuilder.Indent())
        {
            const string entityTypeVariable = "runtimeEntityType";
            var variables = new HashSet<string> { entityTypeVariable };

            CreateAnnotations(
                entityType,
                _annotationCodeGenerator.Generate,
                new CSharpRuntimeAnnotationCodeGeneratorParameters(
                    entityTypeVariable,
                    className,
                    mainBuilder,
                    methodBuilder,
                    namespaces,
                    variables));

            mainBuilder
                .AppendLine()
                .AppendLine("Customize(runtimeEntityType);");
        }

        mainBuilder
            .AppendLine("}")
            .AppendLine()
            .AppendLine("static partial void Customize(RuntimeEntityType runtimeEntityType);");
    }

    private static void CreateAnnotations<TAnnotatable>(
        TAnnotatable annotatable,
        Action<TAnnotatable, CSharpRuntimeAnnotationCodeGeneratorParameters> process,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
        where TAnnotatable : IAnnotatable
    {
        process(
            annotatable,
            parameters with { Annotations = annotatable.GetAnnotations().ToDictionary(a => a.Name, a => a.Value), IsRuntime = false });

        process(
            annotatable,
            parameters with
            {
                Annotations = annotatable.GetRuntimeAnnotations().ToDictionary(a => a.Name, a => a.Value), IsRuntime = true
            });
    }

    private static void AddNamespace(Type type, ISet<string> namespaces)
    {
        if (!string.IsNullOrEmpty(type.Namespace))
        {
            namespaces.Add(type.Namespace);
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GenericTypeArguments)
            {
                AddNamespace(argument, namespaces);
            }
        }

        var sequenceType = type.TryGetSequenceType();
        if (sequenceType != null && sequenceType != type)
        {
            AddNamespace(sequenceType, namespaces);
        }
    }

    private string CreateExpression(Expression? expression, int indent, ISet<string> expressionVariables, ISet<string> namespaces)
    {
        var mainBuilder = new IndentedStringBuilder();
        mainBuilder.IncrementIndent(indent);
        new ExpressionTreePrinter(mainBuilder, expressionVariables, namespaces).Visit(expression);
        return mainBuilder.ToString();
    }

    // TODO: make this print out an expression tree
    private class ExpressionTreePrinter : ExpressionVisitor
    {
        private static readonly List<string> SimpleMethods = new()
        {
            "get_Item",
            "TryReadValue",
            "ReferenceEquals"
        };

        private readonly IndentedStringBuilder _mainBuilder;
        private readonly Dictionary<ParameterExpression, string?> _parametersInScope;
        private readonly List<ParameterExpression> _namelessParameters;
        private readonly List<ParameterExpression> _encounteredParameters;
        private readonly ISet<string> _expressionVariables;
        private readonly ISet<string> _namespaces;


        /// <summary>
        ///     Creates a new instance of the <see cref="ExpressionTreePrinter" /> class.
        /// </summary>
        public ExpressionTreePrinter(
            IndentedStringBuilder mainBuilder,
            ISet<string> expressionVariables,
            ISet<string> namespaces)
        {
            _mainBuilder = mainBuilder;
            _expressionVariables = expressionVariables;
            _namespaces = namespaces;
            _parametersInScope = new Dictionary<ParameterExpression, string?>();
            _namelessParameters = new List<ParameterExpression>();
            _encounteredParameters = new List<ParameterExpression>();
        }

        private int? CharacterLimit { get; set; }
        private bool Verbose { get; set; }

        /// <summary>
        ///     Visit given readonly collection of expression for printing.
        /// </summary>
        /// <param name="items">A collection of items to print.</param>
        /// <param name="joinAction">A join action to use when joining printout of individual item in the collection.</param>
        public virtual void VisitCollection<T>(
            IReadOnlyCollection<T> items,
            Action<ExpressionTreePrinter>? joinAction = null)
            where T : Expression
        {
            joinAction ??= (p => p.Append(", "));

            var first = true;
            foreach (var item in items)
            {
                if (!first)
                {
                    joinAction(this);
                }
                else
                {
                    first = false;
                }

                Visit(item);
            }
        }

        /// <summary>
        ///     Appends a new line to current output being built.
        /// </summary>
        /// <returns>This printer so additional calls can be chained.</returns>
        public virtual ExpressionTreePrinter AppendLine()
        {
            _mainBuilder.AppendLine();
            return this;
        }

        /// <summary>
        ///     Appends the given string and a new line to current output being built.
        /// </summary>
        /// <param name="value">The string to append.</param>
        /// <returns>This printer so additional calls can be chained.</returns>
        public virtual ExpressionVisitor AppendLine(string value)
        {
            _mainBuilder.AppendLine(value);
            return this;
        }

        /// <summary>
        ///     Appends all the lines to current output being built.
        /// </summary>
        /// <param name="value">The string to append.</param>
        /// <param name="skipFinalNewline">If true, then a terminating new line is not added.</param>
        /// <returns>This printer so additional calls can be chained.</returns>
        public virtual ExpressionTreePrinter AppendLines(string value, bool skipFinalNewline = false)
        {
            _mainBuilder.AppendLines(value, skipFinalNewline);
            return this;
        }

        /// <summary>
        ///     Creates a scoped indenter that will increment the indent, then decrement it when disposed.
        /// </summary>
        /// <returns>An indenter.</returns>
        public virtual IDisposable Indent()
            => _mainBuilder.Indent();

        /// <summary>
        ///     Appends the given string to current output being built.
        /// </summary>
        /// <param name="value">The string to append.</param>
        /// <returns>This printer so additional calls can be chained.</returns>
        public virtual ExpressionTreePrinter Append(string value)
        {
            _mainBuilder.Append(value);
            return this;
        }

        /// <summary>
        ///     Creates a printable string representation of the given expression.
        /// </summary>
        /// <param name="expression">The expression to print.</param>
        /// <param name="characterLimit">An optional limit to the number of characters included. Additional output will be truncated.</param>
        /// <returns>The printable representation.</returns>
        public virtual string Print(
            Expression expression,
            int? characterLimit = null)
            => PrintCore(expression, characterLimit, verbose: false);

        /// <summary>
        ///     Creates a printable verbose string representation of the given expression.
        /// </summary>
        /// <param name="expression">The expression to print.</param>
        /// <returns>The printable representation.</returns>
        public virtual string PrintDebug(
            Expression expression)
            => PrintCore(expression, characterLimit: null, verbose: true);

        private string PrintCore(
            Expression expression,
            int? characterLimit,
            bool verbose)
        {
            _mainBuilder.Clear();
            _parametersInScope.Clear();
            _namelessParameters.Clear();
            _encounteredParameters.Clear();

            CharacterLimit = characterLimit;
            Verbose = verbose;

            Visit(expression);

            var queryPlan = PostProcess(_mainBuilder.ToString());

            if (characterLimit != null
                && characterLimit.Value > 0)
            {
                queryPlan = queryPlan.Length > characterLimit
                    ? queryPlan[..characterLimit.Value] + "..."
                    : queryPlan;
            }

            return queryPlan;
        }

        /// <summary>
        ///     Returns binary operator string corresponding to given <see cref="ExpressionType" />.
        /// </summary>
        /// <param name="expressionType">The expression type to generate binary operator for.</param>
        /// <returns>The binary operator string.</returns>
        public virtual string GenerateBinaryOperator(ExpressionType expressionType)
            => _binaryOperandMap[expressionType];

        /// <inheritdoc />
        [return: NotNullIfNotNull("expression")]
        public override Expression? Visit(Expression? expression)
        {
            if (expression == null)
            {
                return null;
            }

            if (CharacterLimit != null
                && _mainBuilder.Length > CharacterLimit.Value)
            {
                return expression;
            }

            switch (expression.NodeType)
            {
                case ExpressionType.AndAlso:
                case ExpressionType.ArrayIndex:
                case ExpressionType.Assign:
                case ExpressionType.Equal:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.NotEqual:
                case ExpressionType.OrElse:
                case ExpressionType.Coalesce:
                case ExpressionType.Add:
                case ExpressionType.Subtract:
                case ExpressionType.Multiply:
                case ExpressionType.Divide:
                case ExpressionType.Modulo:
                case ExpressionType.And:
                case ExpressionType.Or:
                case ExpressionType.ExclusiveOr:
                    VisitBinary((BinaryExpression)expression);
                    break;

                case ExpressionType.Block:
                    VisitBlock((BlockExpression)expression);
                    break;

                case ExpressionType.Conditional:
                    VisitConditional((ConditionalExpression)expression);
                    break;

                case ExpressionType.Constant:
                    VisitConstant((ConstantExpression)expression);
                    break;

                case ExpressionType.Lambda:
                    base.Visit(expression);
                    break;

                case ExpressionType.Goto:
                    VisitGoto((GotoExpression)expression);
                    break;

                case ExpressionType.Label:
                    VisitLabel((LabelExpression)expression);
                    break;

                case ExpressionType.MemberAccess:
                    VisitMember((MemberExpression)expression);
                    break;

                case ExpressionType.MemberInit:
                    VisitMemberInit((MemberInitExpression)expression);
                    break;

                case ExpressionType.Call:
                    VisitMethodCall((MethodCallExpression)expression);
                    break;

                case ExpressionType.New:
                    VisitNew((NewExpression)expression);
                    break;

                case ExpressionType.NewArrayInit:
                    VisitNewArray((NewArrayExpression)expression);
                    break;

                case ExpressionType.Parameter:
                    VisitParameter((ParameterExpression)expression);
                    break;

                case ExpressionType.Convert:
                case ExpressionType.Throw:
                case ExpressionType.Not:
                case ExpressionType.TypeAs:
                case ExpressionType.Quote:
                    VisitUnary((UnaryExpression)expression);
                    break;

                case ExpressionType.Default:
                    VisitDefault((DefaultExpression)expression);
                    break;

                case ExpressionType.Try:
                    VisitTry((TryExpression)expression);
                    break;

                case ExpressionType.Index:
                    VisitIndex((IndexExpression)expression);
                    break;

                case ExpressionType.TypeIs:
                    VisitTypeBinary((TypeBinaryExpression)expression);
                    break;

                case ExpressionType.Switch:
                    VisitSwitch((SwitchExpression)expression);
                    break;

                case ExpressionType.Invoke:
                    VisitInvocation((InvocationExpression)expression);
                    break;

                case ExpressionType.Extension:
                    VisitExtension(expression);
                    break;

                default:
                    UnhandledExpressionType(expression);
                    break;
            }

            return expression;
        }

        /// <inheritdoc />
        protected override Expression VisitBinary(BinaryExpression binaryExpression)
        {
            Visit(binaryExpression.Left);

            if (binaryExpression.NodeType == ExpressionType.ArrayIndex)
            {
                _mainBuilder.Append("[");

                Visit(binaryExpression.Right);

                _mainBuilder.Append("]");
            }
            else
            {
                if (!_binaryOperandMap.TryGetValue(binaryExpression.NodeType, out var operand))
                {
                    UnhandledExpressionType(binaryExpression);
                }
                else
                {
                    _mainBuilder.Append(operand);
                }

                Visit(binaryExpression.Right);
            }

            return binaryExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitBlock(BlockExpression blockExpression)
        {
            AppendLine();
            AppendLine("{");

            using (_mainBuilder.Indent())
            {
                foreach (var variable in blockExpression.Variables)
                {
                    if (!_parametersInScope.ContainsKey(variable))
                    {
                        _parametersInScope.Add(variable, variable.Name);
                        Append(variable.Type.ShortDisplayName());
                        Append(" ");
                        VisitParameter(variable);
                        AppendLine(";");
                    }
                }

                var expressions = blockExpression.Result != null
                    ? blockExpression.Expressions.Except(new[] { blockExpression.Result })
                    : blockExpression.Expressions;

                foreach (var expression in expressions)
                {
                    Visit(expression);
                    AppendLine(";");
                }

                if (blockExpression.Result != null)
                {
                    if (blockExpression.Result.Type != typeof(void))
                    {
                        Append("return ");
                    }

                    Visit(blockExpression.Result);
                    AppendLine(";");
                }
            }

            Append("}");

            return blockExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitConditional(ConditionalExpression conditionalExpression)
        {
            Visit(conditionalExpression.Test);

            _mainBuilder.Append(" ? ");

            Visit(conditionalExpression.IfTrue);

            _mainBuilder.Append(" : ");

            Visit(conditionalExpression.IfFalse);

            return conditionalExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitConstant(ConstantExpression constantExpression)
        {
            if (constantExpression.Value is IPrintableExpression printable)
            {
                printable.Print(this);
            }
            else
            {
                Print(constantExpression.Value);
            }

            return constantExpression;
        }

        private void Print(object? value)
        {
            if (value is IEnumerable enumerable
                && !(value is string))
            {
                _mainBuilder.Append(value.GetType().ShortDisplayName() + " { ");

                var first = true;
                foreach (var item in enumerable)
                {
                    if (first)
                    {
                        first = false;
                    }
                    else
                    {
                        _mainBuilder.Append(", ");
                    }

                    Print(item);
                }

                _mainBuilder.Append(" }");
                return;
            }

            var stringValue = value == null
                ? "null"
                : value.ToString() != value.GetType().ToString()
                    ? value.ToString()
                    : value.GetType().ShortDisplayName();

            if (value is string)
            {
                stringValue = $@"""{stringValue}""";
            }

            _mainBuilder.Append(stringValue ?? "Unknown");
        }

        /// <inheritdoc />
        protected override Expression VisitGoto(GotoExpression gotoExpression)
        {
            AppendLine("return (" + gotoExpression.Target.Type.ShortDisplayName() + ")" + gotoExpression.Target + " {");
            using (_mainBuilder.Indent())
            {
                Visit(gotoExpression.Value);
            }

            _mainBuilder.Append("}");

            return gotoExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitLabel(LabelExpression labelExpression)
        {
            _mainBuilder.Append(labelExpression.Target.ToString());

            return labelExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitLambda<T>(Expression<T> lambdaExpression)
        {
            if (lambdaExpression.Parameters.Count != 1)
            {
                _mainBuilder.Append("(");
            }

            foreach (var parameter in lambdaExpression.Parameters)
            {
                var parameterName = parameter.Name;

                if (!_parametersInScope.ContainsKey(parameter))
                {
                    _parametersInScope.Add(parameter, parameterName);
                }

                Visit(parameter);

                if (parameter != lambdaExpression.Parameters.Last())
                {
                    _mainBuilder.Append(", ");
                }
            }

            if (lambdaExpression.Parameters.Count != 1)
            {
                _mainBuilder.Append(")");
            }

            _mainBuilder.Append(" => ");

            Visit(lambdaExpression.Body);

            foreach (var parameter in lambdaExpression.Parameters)
            {
                // however we don't remove nameless parameters so that they are unique globally, not just within the scope
                _parametersInScope.Remove(parameter);
            }

            return lambdaExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitMember(MemberExpression memberExpression)
        {
            if (memberExpression.Expression != null)
            {
                if (memberExpression.Expression.NodeType == ExpressionType.Convert
                    || memberExpression.Expression is BinaryExpression)
                {
                    _mainBuilder.Append("(");
                    Visit(memberExpression.Expression);
                    _mainBuilder.Append(")");
                }
                else
                {
                    Visit(memberExpression.Expression);
                }
            }
            else
            {
                // ReSharper disable once PossibleNullReferenceException
                _mainBuilder.Append(memberExpression.Member.DeclaringType?.Name ?? "MethodWithoutDeclaringType");
            }

            _mainBuilder.Append("." + memberExpression.Member.Name);

            return memberExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitMemberInit(MemberInitExpression memberInitExpression)
        {
            _mainBuilder.Append("new " + memberInitExpression.Type.ShortDisplayName());

            var appendAction = memberInitExpression.Bindings.Count > 1 ? (Func<string, ExpressionVisitor>)AppendLine : Append;
            appendAction("{ ");
            using (_mainBuilder.Indent())
            {
                for (var i = 0; i < memberInitExpression.Bindings.Count; i++)
                {
                    var binding = memberInitExpression.Bindings[i];
                    if (binding is MemberAssignment assignment)
                    {
                        _mainBuilder.Append(assignment.Member.Name + " = ");
                        Visit(assignment.Expression);
                        appendAction(i == memberInitExpression.Bindings.Count - 1 ? " " : ", ");
                    }
                    else
                    {
                        AppendLine(CoreStrings.UnhandledMemberBinding(binding.BindingType));
                    }
                }
            }

            AppendLine("}");

            return memberInitExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
        {
            if (methodCallExpression.Object != null)
            {
                switch (methodCallExpression.Object)
                {
                    case BinaryExpression:
                    case UnaryExpression:
                        _mainBuilder.Append("(");
                        Visit(methodCallExpression.Object);
                        _mainBuilder.Append(")");
                        break;
                    default:
                        Visit(methodCallExpression.Object);
                        break;
                }

                _mainBuilder.Append(".");
            }

            var methodArguments = methodCallExpression.Arguments.ToList();
            var method = methodCallExpression.Method;

            var extensionMethod = !Verbose
                && methodCallExpression.Arguments.Count > 0
                && method.IsDefined(typeof(ExtensionAttribute), inherit: false);

            if (extensionMethod)
            {
                Visit(methodArguments[0]);
                _mainBuilder.IncrementIndent();
                _mainBuilder.AppendLine();
                _mainBuilder.Append($".{method.Name}");
                methodArguments = methodArguments.Skip(1).ToList();
                if (method.Name == nameof(Enumerable.Cast)
                    || method.Name == nameof(Enumerable.OfType))
                {
                    PrintGenericArguments(method, _mainBuilder);
                }
            }
            else
            {
                if (method.IsStatic)
                {
                    _mainBuilder.Append(method.DeclaringType!.ShortDisplayName()).Append(".");
                }

                _mainBuilder.Append(method.Name);
                PrintGenericArguments(method, _mainBuilder);
            }

            _mainBuilder.Append("(");

            var isSimpleMethodOrProperty = SimpleMethods.Contains(method.Name)
                || methodArguments.Count < 2
                || method.IsEFPropertyMethod();

            var appendAction = isSimpleMethodOrProperty ? (Func<string, ExpressionVisitor>)Append : AppendLine;

            if (methodArguments.Count > 0)
            {
                appendAction("");

                var argumentNames
                    = !isSimpleMethodOrProperty
                        ? extensionMethod
                            ? method.GetParameters().Skip(1).Select(p => p.Name).ToList()
                            : method.GetParameters().Select(p => p.Name).ToList()
                        : new List<string?>();

                IDisposable? indent = null;

                if (!isSimpleMethodOrProperty)
                {
                    indent = _mainBuilder.Indent();
                }

                for (var i = 0; i < methodArguments.Count; i++)
                {
                    var argument = methodArguments[i];

                    if (!isSimpleMethodOrProperty)
                    {
                        _mainBuilder.Append(argumentNames[i] + ": ");
                    }

                    Visit(argument);

                    if (i < methodArguments.Count - 1)
                    {
                        appendAction(", ");
                    }
                }

                if (!isSimpleMethodOrProperty)
                {
                    indent?.Dispose();
                }
            }

            Append(")");

            if (extensionMethod)
            {
                _mainBuilder.DecrementIndent();
            }

            return methodCallExpression;

            static void PrintGenericArguments(MethodInfo method, IndentedStringBuilder stringBuilder)
            {
                if (method.IsGenericMethod)
                {
                    stringBuilder.Append("<");
                    var first = true;
                    foreach (var genericArgument in method.GetGenericArguments())
                    {
                        if (!first)
                        {
                            stringBuilder.Append(", ");
                        }

                        stringBuilder.Append(genericArgument.ShortDisplayName());
                        first = false;
                    }

                    stringBuilder.Append(">");
                }
            }
        }

        /// <inheritdoc />
        protected override Expression VisitNew(NewExpression newExpression)
        {
            _mainBuilder.Append("new ");

            var isComplex = newExpression.Arguments.Count > 1;
            var appendAction = isComplex ? (Func<string, ExpressionVisitor>)AppendLine : Append;

            var isAnonymousType = newExpression.Type.IsAnonymousType();
            if (!isAnonymousType)
            {
                _mainBuilder.Append(newExpression.Type.ShortDisplayName());
                appendAction("(");
            }
            else
            {
                appendAction("{ ");
            }

            IDisposable? indent = null;
            if (isComplex)
            {
                indent = _mainBuilder.Indent();
            }

            for (var i = 0; i < newExpression.Arguments.Count; i++)
            {
                if (newExpression.Members != null)
                {
                    Append(newExpression.Members[i].Name + " = ");
                }

                Visit(newExpression.Arguments[i]);
                appendAction(i == newExpression.Arguments.Count - 1 ? "" : ", ");
            }

            if (isComplex)
            {
                indent?.Dispose();
            }

            _mainBuilder.Append(!isAnonymousType ? ")" : " }");

            return newExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitNewArray(NewArrayExpression newArrayExpression)
        {
            var isComplex = newArrayExpression.Expressions.Count > 1;
            var appendAction = isComplex ? s => AppendLine(s) : (Action<string>)(s => Append(s));

            appendAction("new " + newArrayExpression.Type.GetElementType()!.ShortDisplayName() + "[]");
            appendAction("{ ");

            IDisposable? indent = null;
            if (isComplex)
            {
                indent = _mainBuilder.Indent();
            }

            VisitArguments(newArrayExpression.Expressions, appendAction, lastSeparator: " ");

            if (isComplex)
            {
                indent?.Dispose();
            }

            Append("}");

            return newArrayExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitParameter(ParameterExpression parameterExpression)
        {
            if (_parametersInScope.TryGetValue(parameterExpression, out var parameterName))
            {
                if (parameterName == null)
                {
                    if (!_namelessParameters.Contains(parameterExpression))
                    {
                        _namelessParameters.Add(parameterExpression);
                    }

                    Append("namelessParameter{");
                    Append(_namelessParameters.IndexOf(parameterExpression).ToString());
                    Append("}");
                }
                else if (parameterName.Contains('.'))
                {
                    Append("[");
                    Append(parameterName);
                    Append("]");
                }
                else
                {
                    Append(parameterName);
                }
            }
            else
            {
                if (Verbose)
                {
                    Append("(Unhandled parameter: ");
                    Append(parameterExpression.Name ?? "NoNameParameter");
                    Append(")");
                }
                else
                {
                    Append(parameterExpression.Name ?? "NoNameParameter");
                }
            }

            if (Verbose)
            {
                var parameterIndex = _encounteredParameters.Count;
                if (_encounteredParameters.Contains(parameterExpression))
                {
                    parameterIndex = _encounteredParameters.IndexOf(parameterExpression);
                }
                else
                {
                    _encounteredParameters.Add(parameterExpression);
                }

                _mainBuilder.Append("{" + parameterIndex + "}");
            }

            return parameterExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitUnary(UnaryExpression unaryExpression)
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (unaryExpression.NodeType)
            {
                case ExpressionType.Convert:
                    _mainBuilder.Append("(" + unaryExpression.Type.ShortDisplayName() + ")");

                    if (unaryExpression.Operand is BinaryExpression)
                    {
                        _mainBuilder.Append("(");
                        Visit(unaryExpression.Operand);
                        _mainBuilder.Append(")");
                    }
                    else
                    {
                        Visit(unaryExpression.Operand);
                    }

                    break;

                case ExpressionType.Throw:
                    _mainBuilder.Append("throw ");
                    Visit(unaryExpression.Operand);
                    break;

                case ExpressionType.Not:
                    _mainBuilder.Append("!(");
                    Visit(unaryExpression.Operand);
                    _mainBuilder.Append(")");
                    break;

                case ExpressionType.TypeAs:
                    _mainBuilder.Append("(");
                    Visit(unaryExpression.Operand);
                    _mainBuilder.Append(" as " + unaryExpression.Type.ShortDisplayName() + ")");
                    break;

                case ExpressionType.Quote:
                    Visit(unaryExpression.Operand);
                    break;

                default:
                    UnhandledExpressionType(unaryExpression);
                    break;
            }

            return unaryExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitDefault(DefaultExpression defaultExpression)
        {
            _mainBuilder.Append("default(" + defaultExpression.Type.ShortDisplayName() + ")");

            return defaultExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitTry(TryExpression tryExpression)
        {
            _mainBuilder.Append("try { ");
            Visit(tryExpression.Body);
            _mainBuilder.Append(" } ");

            foreach (var handler in tryExpression.Handlers)
            {
                _mainBuilder.Append("catch (" + handler.Test.Name + ") { ... } ");
            }

            return tryExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitIndex(IndexExpression indexExpression)
        {
            Visit(indexExpression.Object);
            _mainBuilder.Append("[");
            VisitArguments(
                indexExpression.Arguments, s => { _mainBuilder.Append(s); });
            _mainBuilder.Append("]");

            return indexExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitTypeBinary(TypeBinaryExpression typeBinaryExpression)
        {
            _mainBuilder.Append("(");
            Visit(typeBinaryExpression.Expression);
            _mainBuilder.Append(" is " + typeBinaryExpression.TypeOperand.ShortDisplayName() + ")");

            return typeBinaryExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitSwitch(SwitchExpression switchExpression)
        {
            _mainBuilder.Append("switch (");
            Visit(switchExpression.SwitchValue);
            _mainBuilder.AppendLine(")");
            _mainBuilder.AppendLine("{");
            _mainBuilder.IncrementIndent();

            foreach (var @case in switchExpression.Cases)
            {
                foreach (var testValue in @case.TestValues)
                {
                    _mainBuilder.Append("case ");
                    Visit(testValue);
                    _mainBuilder.AppendLine(": ");
                }

                using (_mainBuilder.Indent())
                {
                    Visit(@case.Body);
                }

                _mainBuilder.AppendLine();
            }

            if (switchExpression.DefaultBody != null)
            {
                _mainBuilder.AppendLine("default: ");
                using (_mainBuilder.Indent())
                {
                    Visit(switchExpression.DefaultBody);
                }

                _mainBuilder.AppendLine();
            }

            _mainBuilder.DecrementIndent();
            _mainBuilder.AppendLine("}");

            return switchExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitInvocation(InvocationExpression invocationExpression)
        {
            _mainBuilder.Append("Invoke(");
            Visit(invocationExpression.Expression);

            foreach (var argument in invocationExpression.Arguments)
            {
                _mainBuilder.Append(", ");
                Visit(argument);
            }

            _mainBuilder.Append(")");

            return invocationExpression;
        }

        /// <inheritdoc />
        protected override Expression VisitExtension(Expression extensionExpression)
        {
            if (extensionExpression is IPrintableExpression printable)
            {
                printable.Print(this);
            }
            else
            {
                UnhandledExpressionType(extensionExpression);
            }

            return extensionExpression;
        }

        private void VisitArguments(
            IReadOnlyList<Expression> arguments,
            Action<string> appendAction,
            string lastSeparator = "",
            bool areConnected = false)
        {
            for (var i = 0; i < arguments.Count; i++)
            {
                if (areConnected && i == arguments.Count - 1)
                {
                    Append("");
                }

                Visit(arguments[i]);
                appendAction(i == arguments.Count - 1 ? lastSeparator : ", ");
            }
        }

        private static string PostProcess(string printedExpression)
        {
            var processedPrintedExpression = printedExpression
                .Replace("Microsoft.EntityFrameworkCore.Query.", "")
                .Replace("Microsoft.EntityFrameworkCore.", "")
                .Replace(Environment.NewLine + Environment.NewLine, Environment.NewLine);

            return processedPrintedExpression;
        }

        private void UnhandledExpressionType(Expression expression)
            => AppendLine(expression.ToString());
    }
}
