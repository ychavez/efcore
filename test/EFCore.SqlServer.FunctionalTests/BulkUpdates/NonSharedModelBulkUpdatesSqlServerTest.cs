﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.EntityFrameworkCore.BulkUpdates;

public class NonSharedModelBulkUpdatesSqlServerTest : NonSharedModelBulkUpdatesTestBase
{
    protected override ITestStoreFactory TestStoreFactory
        => SqlServerTestStoreFactory.Instance;

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    public override async Task Delete_aggregate_root_when_eager_loaded_owned_collection(bool async)
    {
        await base.Delete_aggregate_root_when_eager_loaded_owned_collection(async);

        AssertSql(
"""
DELETE FROM [o]
FROM [Owner] AS [o]
""");
    }

    public override async Task Delete_aggregate_root_when_table_sharing_with_owned(bool async)
    {
        await base.Delete_aggregate_root_when_table_sharing_with_owned(async);

        AssertSql(
"""
DELETE FROM [o]
FROM [Owner] AS [o]
""");
    }

    public override async Task Delete_aggregate_root_when_table_sharing_with_non_owned_throws(bool async)
    {
        await base.Delete_aggregate_root_when_table_sharing_with_non_owned_throws(async);

        AssertSql();
    }

    public override async Task Update_non_owned_property_on_entity_with_owned(bool async)
    {
        await base.Update_non_owned_property_on_entity_with_owned(async);

        AssertSql(
"""
UPDATE [o]
SET [o].[Title] = N'SomeValue'
FROM [Owner] AS [o]
""");
    }

    public override async Task Update_non_owned_property_on_entity_with_owned2(bool async)
    {
        await base.Update_non_owned_property_on_entity_with_owned2(async);

        AssertSql(
"""
UPDATE [o]
SET [o].[Title] = COALESCE([o].[Title], N'') + N'_Suffix'
FROM [Owner] AS [o]
""");
    }

    public override async Task Delete_entity_with_auto_include(bool async)
    {
        await base.Delete_entity_with_auto_include(async);

        AssertSql(
"""
DELETE FROM [c]
FROM [Context30572_Principal] AS [c]
LEFT JOIN [Context30572_Dependent] AS [c0] ON [c].[DependentId] = [c0].[Id]
""");
    }

    public override async Task Delete_predicate_based_on_optional_navigation(bool async)
    {
        await base.Delete_predicate_based_on_optional_navigation(async);

        AssertSql(
"""
DELETE FROM [p]
FROM [Posts] AS [p]
LEFT JOIN [Blogs] AS [b] ON [p].[BlogId] = [b].[Id]
WHERE ([b].[Title] IS NOT NULL) AND ([b].[Title] LIKE N'Arthur%')
""");
    }

    public override async Task Update_with_alias_uniquification_in_setter_subquery(bool async)
    {
        await base.Update_with_alias_uniquification_in_setter_subquery(async);

        AssertSql(
"""
UPDATE [o]
SET [o].[Total] = (
    SELECT COALESCE(SUM([o0].[Amount]), 0)
    FROM [OrderProduct] AS [o0]
    WHERE [o].[Id] = [o0].[OrderId])
FROM [Orders] AS [o]
WHERE [o].[Id] = 1
""");
    }

    private void AssertSql(params string[] expected)
        => TestSqlLoggerFactory.AssertBaseline(expected);

    private void AssertExecuteUpdateSql(params string[] expected)
        => TestSqlLoggerFactory.AssertBaseline(expected, forUpdate: true);
}
