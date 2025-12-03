using Microsoft.AspNetCore.Hosting;
using Whisper.net;
using Whisper.net.Ggml;

namespace Project_Advanced.Services
{
    public class WhisperService
    {
        private readonly WhisperFactory _factory;

        public WhisperService(IWebHostEnvironment env)
        {
            var modelPath = Path.Combine(env.ContentRootPath, "Models", "ggml-small.bin");
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Whisper model not found at {modelPath}. Place ggml-small.bin in the Models folder.");
            }

            _factory = WhisperFactory.FromPath(modelPath);
        }

        public async Task<string> TranscribeAsync(Stream audioStream, string language = "en")
        {
            using var processor = _factory.CreateBuilder()
                .WithLanguage(language)
                .Build();

            var result = "";
            await foreach (var segment in processor.ProcessAsync(audioStream))
            {
                result += segment.Text;
            }

            return result.Trim();
        }
    }
}
