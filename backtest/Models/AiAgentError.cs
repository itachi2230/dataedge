using System;

namespace backtest.Models
{
    public sealed class AiAgentError
    {
        public AiAgentError(string message, string details = null, Exception exception = null)
        {
            Message = message;
            Details = details;
            Exception = exception;
        }

        public string Message { get; }
        public string Details { get; }
        public Exception Exception { get; }

        public string ToDisplayText()
        {
            var text = Message;
            if (!string.IsNullOrWhiteSpace(Details)) text += $"\n\nDétails :\n{Details}";
            if (Exception != null) text += $"\n\nType : {Exception.GetType().FullName}";
            return text;
        }
    }
}