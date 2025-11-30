using System;
using System.Collections.Generic;

namespace Project_Advanced.Models
{
    public class ChatConversation
    {
        public int Id { get; set; }

        public string? Title { get; set; }
        public string? IssueContext { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Who created it (optional)
        public string? CreatedByUserId { get; set; }

        public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }

    public class ChatMessage
    {
        public int Id { get; set; }

        public int ConversationId { get; set; }
        public ChatConversation Conversation { get; set; } = null!;

        // "user" or "assistant"
        public string Sender { get; set; } = "user";

        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }
}
