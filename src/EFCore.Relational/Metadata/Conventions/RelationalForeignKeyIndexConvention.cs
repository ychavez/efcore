// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
///     A convention that creates indexes on foreign key properties unless they are already covered by existing non-filtered indexes
///     or keys.
/// </summary>
/// <remarks>
///     See <see href="https://aka.ms/efcore-docs-conventions">Model building conventions</see> for more information and examples.
/// </remarks>
public class RelationalForeignKeyIndexConvention : ForeignKeyIndexConvention, IIndexAnnotationChangedConvention
{
    /// <summary>
    ///     Creates a new instance of <see cref="RelationalForeignKeyIndexConvention" />.
    /// </summary>
    /// <param name="dependencies">Parameter object containing dependencies for this convention.</param>
    public RelationalForeignKeyIndexConvention(ProviderConventionSetBuilderDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    protected override bool AreIndexedBy(
        IReadOnlyList<IConventionProperty> properties,
        bool unique,
        IConventionIndex existingIndex)
        => existingIndex.GetFilter() == null
            && base.AreIndexedBy(properties, unique, existingIndex);

    /// <inheritdoc />
    public virtual void ProcessIndexAnnotationChanged(
        IConventionIndexBuilder indexBuilder,
        string name,
        IConventionAnnotation? annotation,
        IConventionAnnotation? oldAnnotation,
        IConventionContext<IConventionAnnotation> context)
    {
        if (name == RelationalAnnotationNames.Filter)
        {
            RebuildForeignKeyIndexes(indexBuilder.Metadata);
        }
    }
}
