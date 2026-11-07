using AlfMcp.Tools;
using ModelContextProtocol;
using System.Text.Json;

namespace AlfMcp.Tests;

public class JsonlTests : IDisposable {

    private readonly Fixture fixture = new();

    public void Dispose() => fixture.Dispose();

    private static JsonElement Json(string json) {
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private string Run(string action, string? collection = null, string? target = null, string? data = null, string? where = null, int? limit = null, int? offset = null, string? prefix = null) {
        return fixture.Get<DataTool>().Run(action, collection, target, data is null ? null : Json(data), where is null ? null : Json(where), limit, offset, prefix);
    }

    [Fact]
    public void Insert_AssignsIncreasingIds_AndCreatesTheCollection() {
        var first = Run("insert", "people", data: """{"name":"Ayşe","city":"İzmir"}""");
        var second = Run("insert", "people", data: """[{"name":"Ali"},{"name":"Veli"}]""");

        Assert.Contains("\"ids\":[1]", first);
        Assert.Contains("\"ids\":[2,3]", second);
        Assert.Contains("\"count\":3", Run("collections"));
    }

    [Fact]
    public void Turkish_Characters_AreStoredReadable() {
        Run("insert", "people", data: """{"name":"Çağlar Şişli"}""");

        var line = File.ReadAllText(Path.Combine(fixture.Store.Home, "people.jsonl"));

        Assert.Contains("Çağlar Şişli", line);
    }

    [Fact]
    public void List_Count_AndWhere_FilterByEquality() {
        Run("insert", "people", data: """[{"name":"Ayşe","city":"İzmir"},{"name":"Ali","city":"Ankara"},{"name":"Veli","city":"İzmir"}]""");

        Assert.Contains("\"count\":2", Run("count", "people", where: """{"city":"İzmir"}"""));
        Assert.Contains("\"count\":3", Run("count", "people"));
        Assert.DoesNotContain("Ali", Run("list", "people", where: """{"city":"İzmir"}"""));
        Assert.Equal(1, JsonDocument.Parse(Run("list", "people", limit: 1)).RootElement.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public void List_PagesWithLimitAndOffset_AndReportsTotal() {
        Run("insert", "numbers", data: JsonSerializer.Serialize(Enumerable.Range(1, 25).Select(number => new { n = number })));

        var reply = JsonDocument.Parse(Run("list", "numbers", limit: 10, offset: 20)).RootElement;

        Assert.Equal(25, reply.GetProperty("total").GetInt32());
        Assert.Equal(5, reply.GetProperty("rows").GetArrayLength());
        Assert.Equal(21, reply.GetProperty("rows")[0].GetProperty("n").GetInt32());
    }

    [Fact]
    public void UpdateAndDelete_TargetRecordsByWhere_IncludingId() {
        Run("insert", "people", data: """[{"name":"Ayşe","age":25},{"name":"Ali","age":30},{"name":"Veli","age":30}]""");

        var updatedOne = Run("update", "people", where: """{"_id":1}""", data: """{"age":26}""");
        var updatedMany = Run("update", "people", where: """{"age":30}""", data: """{"active":true}""");
        var got = Run("list", "people", where: """{"_id":1}""");
        var deletedById = Run("delete", "people", where: """{"_id":1}""");
        var deletedByWhere = Run("delete", "people", where: """{"active":true}""");

        Assert.Contains("\"updated\":1", updatedOne);
        Assert.Contains("\"updated\":2", updatedMany);
        Assert.Contains("\"age\":26", got);
        Assert.Contains("Ayşe", got);
        Assert.Contains("\"deleted\":1", deletedById);
        Assert.Contains("\"deleted\":2", deletedByWhere);
        Assert.Contains("\"count\":0", Run("count", "people"));
    }

    [Fact]
    public void UpdateAndDelete_WithoutFilter_AreRejected() {
        Run("insert", "people", data: """{"name":"Ayşe"}""");

        Assert.Throws<McpException>(() => Run("delete", "people"));
        Assert.Throws<McpException>(() => Run("update", "people", data: """{"name":"X"}"""));
        Assert.Throws<McpException>(() => Run("delete", "people", where: "{}"));
        Assert.Contains("\"count\":1", Run("count", "people"));
    }

    [Fact]
    public void CollectionActions_CreateCopyRenameDrop() {
        Run("create", "a");
        Run("insert", "a", data: """{"x":1}""");
        Run("copy", "a", "b");
        Run("rename", "b", "c");

        Assert.Contains("\"name\":\"a\"", Run("collections"));
        Assert.Contains("\"name\":\"c\"", Run("collections"));
        Assert.DoesNotContain("\"name\":\"b\"", Run("collections"));
        Assert.Contains("\"count\":1", Run("count", "c"));
        Assert.Throws<McpException>(() => Run("create", "a"));
        Assert.Throws<McpException>(() => Run("copy", "a", "c"));

        Run("drop", "c");

        Assert.DoesNotContain("\"name\":\"c\"", Run("collections"));
        Assert.Throws<McpException>(() => Run("count", "c"));
    }

    [Fact]
    public void InvalidNames_AreRejected() {
        Assert.Throws<McpException>(() => Run("create", "../evil"));
        Assert.Throws<McpException>(() => Run("insert", "ok", data: "[]"));
    }

    [Fact]
    public void Collections_UseNamespacePrefixes_AndCollectionsCanFilterByPrefix() {
        Run("insert", "invoices-payments", data: """{"x":1}""");
        Run("insert", "hr-people", data: """{"x":1}""");

        var invoices = Run("collections", prefix: "invoices");

        Assert.Contains("invoices-payments", invoices);
        Assert.DoesNotContain("hr-people", invoices);
        Assert.True(File.Exists(Path.Combine(fixture.Store.Home, "invoices-payments.jsonl")));
        Assert.Throws<McpException>(() => Run("insert", "../evil", data: """{"x":1}"""));
        Assert.Throws<McpException>(() => Run("insert", "sub/folder", data: """{"x":1}"""));
        Assert.Throws<McpException>(() => Run("insert", "a-b-c-d-e-f", data: """{"x":1}"""));
    }

}
