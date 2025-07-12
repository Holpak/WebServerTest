global using Microsoft.AspNetCore.Mvc.Testing;
global using Xunit;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_Returns_Ok()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/create", null);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var id = JsonSerializer.Deserialize<int>(content);
        Assert.True(id > 0);
    }

    [Fact]
    public async Task Edit_Returns_Ok()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/create", null);
        var content = await response.Content.ReadAsStringAsync();
        var id = JsonSerializer.Deserialize<int>(content);

        var editResponse = await client.PutAsync($"/edit/{id}?newState=updated", null);
        editResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task History_Returns_Ok()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/create", null);
        var content = await response.Content.ReadAsStringAsync();
        var id = JsonSerializer.Deserialize<int>(content);

        await client.PutAsync($"/edit/{id}?newState=updated", null);

        var historyResponse = await client.GetAsync($"/history/{id}");
        historyResponse.EnsureSuccessStatusCode();

        var historyContent = await historyResponse.Content.ReadAsStringAsync();
        var history = JsonSerializer.Deserialize<List<StateHistoryEntry>>(historyContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.Equal(2, history.Count);
        Assert.Equal("created", history[0].State);
        Assert.Equal("updated", history[1].State);
    }
}
