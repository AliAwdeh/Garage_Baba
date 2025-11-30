using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_Advanced.Data;
using Project_Advanced.Models;
using Project_Advanced.Models.ViewModels;

namespace Project_Advanced.Controllers
{
    [Authorize(Roles = "Admin")]
    public class ChatController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly UserManager<IdentityUser> _userManager;
        private const int PageSize = 10;

        public ChatController(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            UserManager<IdentityUser> userManager)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _config = config;
            _userManager = userManager;
        }

        // List conversations + search
        public async Task<IActionResult> Index(string? search, int page = 1)
        {
            var query = _context.ChatConversations
                .Include(c => c.Messages)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(c =>
                    (c.Title != null && c.Title.Contains(search)) ||
                    (c.IssueContext != null && c.IssueContext.Contains(search)));
            }

            var conversations = await PaginatedList<ChatConversation>.CreateAsync(
                query.OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt),
                page,
                PageSize);
            ViewBag.Search = search;
            ViewData["page"] = page;

            return View(conversations);
        }

        // Create a new conversation (with initial context)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string? title, string? issueContext)
        {
            var user = await _userManager.GetUserAsync(User);

            var conv = new ChatConversation
            {
                Title = string.IsNullOrWhiteSpace(title) ? "New conversation" : title,
                IssueContext = issueContext,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedByUserId = user?.Id
            };

            _context.ChatConversations.Add(conv);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Conversation), new { id = conv.Id });
        }

        // Create conversation, seed context, and start with a message (AJAX)
        [HttpPost]
        public async Task<IActionResult> CreateAndStartChat(string title, string issueContext)
        {
            var user = await _userManager.GetUserAsync(User);
            var conv = new ChatConversation
            {
                Title = string.IsNullOrWhiteSpace(title) ? "New conversation" : title,
                IssueContext = issueContext,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedByUserId = user?.Id
            };

            _context.ChatConversations.Add(conv);
            await _context.SaveChangesAsync();

            // Seed initial message
            var userMsg = new ChatMessage
            {
                ConversationId = conv.Id,
                Sender = "user",
                Content = "hey checkout this new workorder",
                CreatedAt = DateTime.UtcNow
            };
            _context.ChatMessages.Add(userMsg);
            await _context.SaveChangesAsync();

            // Kick off assistant reply using just the initial message
            try
            {
                var assistantReply = await GetAssistantReplyAsync(conv, new[] { userMsg });
                var aiMsg = new ChatMessage
                {
                    ConversationId = conv.Id,
                    Sender = "assistant",
                    Content = assistantReply,
                    CreatedAt = DateTime.UtcNow
                };
                _context.ChatMessages.Add(aiMsg);
                conv.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            catch
            {
                // swallow errors here to avoid blocking redirect; conversation exists with initial message
            }

            return Json(new { conversationId = conv.Id });
        }

        // View a conversation + messages
        public async Task<IActionResult> Conversation(int id)
        {
            var conv = await _context.ChatConversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (conv == null) return NotFound();

            // Order messages chronologically
            conv.Messages = conv.Messages
                .OrderBy(m => m.CreatedAt)
                .ToList();

            return View(conv);
        }

        // Update context / title
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateContext(int id, string? title, string? issueContext)
        {
            var conv = await _context.ChatConversations.FindAsync(id);
            if (conv == null) return NotFound();

            conv.Title = string.IsNullOrWhiteSpace(title) ? conv.Title : title;
            conv.IssueContext = issueContext;
            conv.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Conversation), new { id });
        }

        private async Task<string> GetAssistantReplyAsync(ChatConversation conv, IEnumerable<ChatMessage> orderedMessages)
        {
            var messages = new System.Collections.Generic.List<object>();

            var systemContent =
                "You are an automotive technician assistant., you help the lead mechanic " +
                "Use the full conversation history and issue context to help diagnose car issues and suggest checks.";
            if (!string.IsNullOrWhiteSpace(conv.IssueContext))
            {
                systemContent += "\n\nIssue context:\n" + conv.IssueContext;
            }

            messages.Add(new { role = "system", content = systemContent });

            foreach (var m in orderedMessages)
            {
                var role = m.Sender == "assistant" ? "assistant" : "user";
                messages.Add(new { role, content = m.Content });
            }

            var client = _httpClientFactory.CreateClient("ExternalAI");
            var model = _config["ExternalAI:Model"] ?? "baba:latest";

            var payload = new
            {
                model = model,
                messages = messages,
                stream = false
            };

            var jsonString = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("/api/chat", content);
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(errorText);
            }

            var responseText = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            var assistantReply = "No reply generated.";
            if (root.TryGetProperty("message", out var msgEl) &&
                msgEl.TryGetProperty("content", out var contentProp))
            {
                assistantReply = contentProp.GetString() ?? assistantReply;
            }

            return assistantReply;
        }

        // Send a message and get AI reply
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessage(int id, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return RedirectToAction(nameof(Conversation), new { id });
            }

            var conv = await _context.ChatConversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (conv == null) return NotFound();

            // Save user message
            var userMsg = new ChatMessage
            {
                ConversationId = conv.Id,
                Sender = "user",
                Content = message,
                CreatedAt = DateTime.UtcNow
            };
            _context.ChatMessages.Add(userMsg);
            await _context.SaveChangesAsync();

            var ordered = conv.Messages
                .OrderBy(m => m.CreatedAt)
                .Concat(new[] { userMsg }); // include new message

            var assistantReply = await GetAssistantReplyAsync(conv, ordered);

            // Save assistant message
            var aiMsg = new ChatMessage
            {
                ConversationId = conv.Id,
                Sender = "assistant",
                Content = assistantReply,
                CreatedAt = DateTime.UtcNow
            };
            _context.ChatMessages.Add(aiMsg);

            conv.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Conversation), new { id });
        }

        // AJAX: Send message and get AI reply (returns JSON)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessageApi(int id, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return BadRequest(new { error = "Message is required." });
            }

            var conv = await _context.ChatConversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (conv == null) return NotFound();

            var userMsg = new ChatMessage
            {
                ConversationId = conv.Id,
                Sender = "user",
                Content = message,
                CreatedAt = DateTime.UtcNow
            };
            _context.ChatMessages.Add(userMsg);
            await _context.SaveChangesAsync();

            var ordered = conv.Messages
                .OrderBy(m => m.CreatedAt)
                .Concat(new[] { userMsg });

            string assistantReply;
            try
            {
                assistantReply = await GetAssistantReplyAsync(conv, ordered);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }

            var aiMsg = new ChatMessage
            {
                ConversationId = conv.Id,
                Sender = "assistant",
                Content = assistantReply,
                CreatedAt = DateTime.UtcNow
            };
            _context.ChatMessages.Add(aiMsg);

            conv.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Json(new
            {
                user = new { content = userMsg.Content, createdAt = userMsg.CreatedAt.ToLocalTime().ToString("g") },
                assistant = new { content = aiMsg.Content, createdAt = aiMsg.CreatedAt.ToLocalTime().ToString("g") }
            });
        }
    }
}
