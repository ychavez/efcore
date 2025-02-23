﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.EntityFrameworkCore.Query;

public class JsonQueryAdHocSqlServerTest : JsonQueryAdHocTestBase
{
    public JsonQueryAdHocSqlServerTest(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override ITestStoreFactory TestStoreFactory
        => SqlServerTestStoreFactory.Instance;

    protected override void Seed29219(MyContext29219 ctx)
    {
        var entity1 = new MyEntity29219
        {
            Id = 1,
            Reference = new MyJsonEntity29219 { NonNullableScalar = 10, NullableScalar = 11 },
            Collection = new List<MyJsonEntity29219>
            {
                new MyJsonEntity29219 { NonNullableScalar = 100, NullableScalar = 101 },
                new MyJsonEntity29219 { NonNullableScalar = 200, NullableScalar = 201 },
                new MyJsonEntity29219 { NonNullableScalar = 300, NullableScalar = null },
            }
        };

        var entity2 = new MyEntity29219
        {
            Id = 2,
            Reference = new MyJsonEntity29219 { NonNullableScalar = 20, NullableScalar = null },
            Collection = new List<MyJsonEntity29219>
            {
                new MyJsonEntity29219 { NonNullableScalar = 1001, NullableScalar = null },
            }
        };

        ctx.Entities.AddRange(entity1, entity2);
        ctx.SaveChanges();

        ctx.Database.ExecuteSqlRaw(@"INSERT INTO [Entities] ([Id], [Reference], [Collection])
VALUES(3, N'{{ ""NonNullableScalar"" : 30 }}', N'[{{ ""NonNullableScalar"" : 10001 }}]')");
    }

    protected override void Seed30028(MyContext30028 ctx)
    {
        // complete
        ctx.Database.ExecuteSqlRaw(@"INSERT INTO [Entities] ([Id], [Json])
VALUES(
1,
N'{{""RootName"":""e1"",""Collection"":[{{""BranchName"":""e1 c1"",""Nested"":{{""LeafName"":""e1 c1 l""}}}},{{""BranchName"":""e1 c2"",""Nested"":{{""LeafName"":""e1 c2 l""}}}}],""OptionalReference"":{{""BranchName"":""e1 or"",""Nested"":{{""LeafName"":""e1 or l""}}}},""RequiredReference"":{{""BranchName"":""e1 rr"",""Nested"":{{""LeafName"":""e1 rr l""}}}}}}')");

        // missing collection
        ctx.Database.ExecuteSqlRaw(@"INSERT INTO [Entities] ([Id], [Json])
VALUES(
2,
N'{{""RootName"":""e2"",""OptionalReference"":{{""BranchName"":""e2 or"",""Nested"":{{""LeafName"":""e2 or l""}}}},""RequiredReference"":{{""BranchName"":""e2 rr"",""Nested"":{{""LeafName"":""e2 rr l""}}}}}}')");

        // missing optional reference
        ctx.Database.ExecuteSqlRaw(@"INSERT INTO [Entities] ([Id], [Json])
VALUES(
3,
N'{{""RootName"":""e3"",""Collection"":[{{""BranchName"":""e3 c1"",""Nested"":{{""LeafName"":""e3 c1 l""}}}},{{""BranchName"":""e3 c2"",""Nested"":{{""LeafName"":""e3 c2 l""}}}}],""RequiredReference"":{{""BranchName"":""e3 rr"",""Nested"":{{""LeafName"":""e3 rr l""}}}}}}')");

        // missing required reference
        ctx.Database.ExecuteSqlRaw(@"INSERT INTO [Entities] ([Id], [Json])
VALUES(
4,
N'{{""RootName"":""e4"",""Collection"":[{{""BranchName"":""e4 c1"",""Nested"":{{""LeafName"":""e4 c1 l""}}}},{{""BranchName"":""e4 c2"",""Nested"":{{""LeafName"":""e4 c2 l""}}}}],""OptionalReference"":{{""BranchName"":""e4 or"",""Nested"":{{""LeafName"":""e4 or l""}}}}}}')");
    }
}
