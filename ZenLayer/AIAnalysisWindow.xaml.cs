using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZenLayer
{
    public partial class AIAnalysisWindow : Window
    {
        private Bitmap _screenshot;
        private BitmapSource _bitmapSource;
        private HttpClient _httpClient;
        private readonly string GEMINI_API_KEY;
        private const string GEMINI_API_URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";

        public AIAnalysisWindow(Bitmap screenshot, BitmapSource bitmapSource)
        {
            _screenshot = screenshot;
            _bitmapSource = bitmapSource;
            _httpClient = new HttpClient();
            GEMINI_API_KEY = LoadApiKeyFromConfig();
            InitializeComponent();
            SetupUI();
        }

        public AIAnalysisWindow()
        {
            InitializeComponent();
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


        private void SetupUI()
        {
            // Set the screenshot image
            var screenshotImage = FindName("ScreenshotImage") as System.Windows.Controls.Image;
            if (screenshotImage != null)
            {
                screenshotImage.Source = _bitmapSource;
            }

            // Setup quick prompts
            SetupQuickPrompts();

            // Setup event handlers
            var analyzeButton = FindName("AnalyzeButton") as System.Windows.Controls.Button;
            var promptTextBox = FindName("PromptTextBox") as System.Windows.Controls.TextBox;
            var responseTextBox = FindName("ResponseTextBox") as System.Windows.Controls.TextBox;
            var copyButton = FindName("CopyResponseButton") as System.Windows.Controls.Button;
            var saveButton = FindName("SaveResponseButton") as System.Windows.Controls.Button;

            if (analyzeButton != null && promptTextBox != null && responseTextBox != null)
            {
                analyzeButton.Click += async (s, e) => await AnalyzeImage(promptTextBox, responseTextBox, analyzeButton);
            }

            if (copyButton != null && responseTextBox != null)
            {
                copyButton.Click += (s, e) => CopyResponseToClipboard(responseTextBox);
            }

            if (saveButton != null && responseTextBox != null)
            {
                saveButton.Click += (s, e) => SaveResponseToFile(responseTextBox);
            }
        }

        private void SetupQuickPrompts()
        {
            var quickPromptsPanel = FindName("QuickPromptsPanel") as WrapPanel;
            if (quickPromptsPanel == null) return;

            string[] quickPrompts = {
                "Describe this image",
                "Extract text from this image",
                "Analyze the UI/UX design",
                "Identify any issues or bugs",
                "Suggest improvements"
            };

            foreach (var prompt in quickPrompts)
            {
                var button = new System.Windows.Controls.Button
                {
                    Content = prompt,
                    Style = (Style)FindResource("ModernButtonStyle")
                };
                button.Click += (s, e) => SetCustomPrompt(prompt);
                quickPromptsPanel.Children.Add(button);
            }
        }

        private void SetCustomPrompt(string prompt)
        {
            var promptTextBox = FindName("PromptTextBox") as System.Windows.Controls.TextBox;
            if (promptTextBox != null)
            {
                promptTextBox.Text = prompt;
            }
        }

        private async Task AnalyzeImage(
            System.Windows.Controls.TextBox promptTextBox,
            System.Windows.Controls.TextBox responseTextBox,
            System.Windows.Controls.Button analyzeButton)
        {
            var copyButton = FindName("CopyResponseButton") as System.Windows.Controls.Button;
            var saveButton = FindName("SaveResponseButton") as System.Windows.Controls.Button;

            try
            {
                // Disable all interactive buttons
                analyzeButton.Content = "🔄 Analyzing...";
                analyzeButton.IsEnabled = false;
                responseTextBox.Text = "AI is analyzing your screenshot...";

                if (copyButton != null) copyButton.IsEnabled = false;
                if (saveButton != null) saveButton.IsEnabled = false;

                // Convert screenshot to Base64
                string base64Image = BitmapToBase64(_screenshot);

                var requestBody = new
                {
                    contents = new[]
                    {
                new
                {
                    parts = new object[]
                    {
                        new { text = promptTextBox.Text },
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

                string json = JsonConvert.SerializeObject(requestBody);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{GEMINI_API_URL}?key={GEMINI_API_KEY}", content);
                string responseString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var geminiResponse = JsonConvert.DeserializeObject<GeminiResponse>(responseString);
                    string aiText = geminiResponse?.candidates?[0]?.content?.parts?[0]?.text;

                    if (!string.IsNullOrWhiteSpace(aiText))
                    {
                        responseTextBox.Text = aiText;

                        if (copyButton != null) copyButton.IsEnabled = true;
                        if (saveButton != null) saveButton.IsEnabled = true;
                    }
                    else
                    {
                        responseTextBox.Text = "No meaningful response received.";
                    }
                }
                else
                {
                    responseTextBox.Text = $"Error: {response.StatusCode}\n{responseString}";
                }
            }
            catch (Exception ex)
            {
                responseTextBox.Text = $"Error occurred: {ex.Message}";
            }
            finally
            {
                analyzeButton.Content = "🚀 Analyze with AI";
                analyzeButton.IsEnabled = true;
            }
        }

        private void CopyResponseToClipboard(System.Windows.Controls.TextBox responseTextBox)
        {
            try
            {
                if (!string.IsNullOrEmpty(responseTextBox.Text))
                {
                    System.Windows.Forms.Clipboard.SetText(responseTextBox.Text);
                    System.Windows.MessageBox.Show("Response copied to clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveResponseToFile(System.Windows.Controls.TextBox responseTextBox)
        {
            try
            {
                if (string.IsNullOrEmpty(responseTextBox.Text))
                {
                    System.Windows.MessageBox.Show("No response to save!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    DefaultExt = "txt",
                    FileName = $"AI_Analysis_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    File.WriteAllText(saveFileDialog.FileName, responseTextBox.Text);
                    System.Windows.MessageBox.Show($"Response saved to: {saveFileDialog.FileName}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to save file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string BitmapToBase64(Bitmap bitmap)
        {
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                byte[] imageBytes = ms.ToArray();
                return Convert.ToBase64String(imageBytes);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _httpClient?.Dispose();
            base.OnClosed(e);
        }
    }

    // Response model classes for JSON deserialization
    public class GeminiResponse
    {
        public Candidate[] candidates { get; set; }
    }

    public class Candidate
    {
        public Content content { get; set; }
    }

    public class Content
    {
        public Part[] parts { get; set; }
    }

    public class Part
    {
        public string text { get; set; }
    }
}