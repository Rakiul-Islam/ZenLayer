using Newtonsoft.Json.Linq;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZenLayer
{
    public class GeminiTextExtractor
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public GeminiTextExtractor()
        {
            _apiKey = LoadApiKeyFromConfig();
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }


        public async Task<string> ExtractTextFromImageAsync(Bitmap image)
        {
            try
            {
                // Convert bitmap to base64
                string base64Image = ConvertBitmapToBase64(image);

                // Create request payload
                var requestPayload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = "Extract all the text in the image and give only the text do not write extra stuff. If you dont see any text just say \"No text found.\"" },
                                new
                                {
                                    inline_data = new
                                    {
                                        mime_type = "image/png",
                                        data = base64Image
                                    }
                                }
                            }
                        }
                    }
                };

                // Serialize request
                string jsonRequest = JsonSerializer.Serialize(requestPayload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                // Create HTTP request
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";

                // Send request
                var response = await _httpClient.PostAsync(url, content);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"API request failed: {response.StatusCode} - {responseContent}");
                }

                // Parse response
                var jsonResponse = JsonDocument.Parse(responseContent);

                if (jsonResponse.RootElement.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var content_prop) &&
                        content_prop.TryGetProperty("parts", out var parts) &&
                        parts.GetArrayLength() > 0)
                    {
                        var firstPart = parts[0];
                        if (firstPart.TryGetProperty("text", out var textElement))
                        {
                            return textElement.GetString()?.Trim() ?? "";
                        }
                    }
                }

                throw new Exception("No text found in the API response");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to extract text from image: {ex.Message}", ex);
            }
        }

        private string LoadApiKeyFromConfig()
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = "ZenLayer.config.json"; // Namespace.FileName

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            using (StreamReader reader = new StreamReader(stream))
            {
                var json = reader.ReadToEnd();
                var obj = JObject.Parse(json);
                return obj["GeminiApiKey"]?.ToString();
            }
        }

        private string ConvertBitmapToBase64(Bitmap bitmap)
        {
            using (var memoryStream = new MemoryStream())
            {
                bitmap.Save(memoryStream, ImageFormat.Png);
                byte[] imageBytes = memoryStream.ToArray();
                return Convert.ToBase64String(imageBytes);
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    // Configuration class for API key
    //public static class GeminiConfig
    //{
    //    // You should set this from app.config or environment variable
    //    public static string ApiKey { get; set; } = "YOUR_GEMINI_API_KEY_HERE";
    //}
}