using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Project_Advanced.Controllers
{
    [Authorize]
    public class IssueAssistantController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public IssueAssistantController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Suggest(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return BadRequest("Description is required.");

            var client = _httpClientFactory.CreateClient("ExternalAI");
            var model = _config["ExternalAI:Model"] ?? "baba:latest";

            var payload = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = "You are an automotive technician assistant. Given a customer description of a car problem, reply with a short probable problem summary and recommended checks in a single paragraph." },
                    new { role = "user", content = description }
                },
                stream = false
            };

            var jsonString = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

            // POST /api/chat on your Ollama reverse proxy
            var response = await client.PostAsync("/api/chat", content);
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, errorText);
            }

            var responseText = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            string suggestion = "";
            if (root.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var contentProp))
            {
                suggestion = contentProp.GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(suggestion))
                suggestion = "No suggestion generated from AI.";

            return Json(new { suggestion });
        }
    }
}
