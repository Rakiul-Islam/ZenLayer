using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MarkdownSharp;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfImage = System.Windows.Controls.Image;
using WpfWebBrowser = System.Windows.Controls.WebBrowser;
using WinFormsSaveFileDialog = System.Windows.Forms.SaveFileDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace ZenLayer
{
    public partial class AIAnalysisWindow : Window
    {
        private Bitmap _screenshot;
        private BitmapSource _bitmapSource;
        private HttpClient _httpClient;
        private readonly string GEMINI_API_KEY;
        private const string GEMINI_API_URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";
        private Markdown _markdownProcessor;
        private string _lastResponse = "";

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
            var resourceName = "ZenLayer.config.json";

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
            var screenshotImage = FindName("ScreenshotImage") as WpfImage;
            if (screenshotImage != null)
            {
                screenshotImage.Source = _bitmapSource;
            }

            SetupQuickPrompts();

            var analyzeButton = FindName("AnalyzeButton") as WpfButton;
            var promptTextBox = FindName("PromptTextBox") as WpfTextBox;
            var responseWebBrowser = FindName("ResponseWebBrowser") as WpfWebBrowser;
            var copyButton = FindName("CopyResponseButton") as WpfButton;
            var saveButton = FindName("SaveResponseButton") as WpfButton;

            _markdownProcessor = new Markdown();

            if (analyzeButton != null && promptTextBox != null && responseWebBrowser != null)
            {
                analyzeButton.Click += async (s, e) => await AnalyzeImage(promptTextBox, responseWebBrowser, analyzeButton);
            }

            if (copyButton != null)
            {
                copyButton.Click += (s, e) => CopyResponseToClipboard();
            }

            if (saveButton != null)
            {
                saveButton.Click += (s, e) => SaveResponseToFile();
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
                var button = new WpfButton
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
            var promptTextBox = FindName("PromptTextBox") as WpfTextBox;
            if (promptTextBox != null)
            {
                promptTextBox.Text = prompt;
            }
        }

        private async Task AnalyzeImage(WpfTextBox promptTextBox, WpfWebBrowser responseWebBrowser, WpfButton analyzeButton)
        {
            var copyButton = FindName("CopyResponseButton") as WpfButton;
            var saveButton = FindName("SaveResponseButton") as WpfButton;

            try
            {
                analyzeButton.Content = "🔄 Analyzing...";
                analyzeButton.IsEnabled = false;
                DisplayFormattedContentInWebBrowser(responseWebBrowser, "AI is analyzing your screenshot...", false);

                if (copyButton != null) copyButton.IsEnabled = false;
                if (saveButton != null) saveButton.IsEnabled = false;

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
                        _lastResponse = aiText;
                        DisplayFormattedContentInWebBrowser(responseWebBrowser, aiText, true);

                        if (copyButton != null) copyButton.IsEnabled = true;
                        if (saveButton != null) saveButton.IsEnabled = true;
                    }
                    else
                    {
                        _lastResponse = "No meaningful response received.";
                        DisplayFormattedContentInWebBrowser(responseWebBrowser, _lastResponse, false);
                    }
                }
                else
                {
                    _lastResponse = $"Error: {response.StatusCode}\n{responseString}";
                    DisplayFormattedContentInWebBrowser(responseWebBrowser, _lastResponse, false);
                }
            }
            catch (Exception ex)
            {
                _lastResponse = $"Error occurred: {ex.Message}";
                DisplayFormattedContentInWebBrowser(responseWebBrowser, _lastResponse, false);
            }
            finally
            {
                analyzeButton.Content = "🚀 Analyze with AI";
                analyzeButton.IsEnabled = true;
            }
        }

        public class ContentSection
        {
            public string Content { get; set; }
            public bool IsCode { get; set; }
            public string Language { get; set; }
        }

        private List<ContentSection> ParseMarkdownContent(string content)
        {
            var sections = new List<ContentSection>();

            // Use a more precise regex that captures code blocks with their exact content
            var codeBlockPattern = @"```(\w*)?\r?\n(.*?)\r?\n```";
            var matches = Regex.Matches(content, codeBlockPattern, RegexOptions.Singleline);

            int lastIndex = 0;

            foreach (Match match in matches)
            {
                // Add any text before this code block
                if (match.Index > lastIndex)
                {
                    string textBefore = content.Substring(lastIndex, match.Index - lastIndex).Trim();
                    if (!string.IsNullOrEmpty(textBefore))
                    {
                        sections.Add(new ContentSection
                        {
                            Content = textBefore,
                            IsCode = false
                        });
                    }
                }

                // Add the code block - preserve exact formatting including all newlines and spaces
                string language = match.Groups[1].Value.Trim();
                string codeContent = match.Groups[2].Value; // Don't trim this - preserve exact formatting

                sections.Add(new ContentSection
                {
                    Content = codeContent,
                    IsCode = true,
                    Language = !string.IsNullOrEmpty(language) ? language : "text"
                });

                lastIndex = match.Index + match.Length;
            }

            // Add any remaining text after the last code block
            if (lastIndex < content.Length)
            {
                string textAfter = content.Substring(lastIndex).Trim();
                if (!string.IsNullOrEmpty(textAfter))
                {
                    sections.Add(new ContentSection
                    {
                        Content = textAfter,
                        IsCode = false
                    });
                }
            }

            return sections;
        }

        private void DisplayFormattedContentInWebBrowser(WpfWebBrowser webBrowser, string content, bool isMarkdown)
        {
            string htmlContent;

            if (isMarkdown)
            {
                var sections = ParseMarkdownContent(content);
                var htmlBody = new StringBuilder();

                for (int i = 0; i < sections.Count; i++)
                {
                    var section = sections[i];

                    if (section.IsCode)
                    {
                        string codeId = $"code_{i}";
                        string buttonId = $"btn_{i}";

                        // Preserve ALL formatting - no HTML encoding that might strip newlines
                        // Just escape the minimal required characters for HTML display
                        string escapedCode = section.Content
                            .Replace("&", "&amp;")
                            .Replace("<", "&lt;")
                            .Replace(">", "&gt;");

                        // For JavaScript string, properly escape for JSON
                        string jsEscapedCode = JsonConvert.SerializeObject(section.Content);

                        htmlBody.AppendLine($@"
                        <div class='code-container'>
                            <button class='code-toggle-btn' id='{buttonId}' onclick='toggleCodeBlock(""{codeId}"", ""{buttonId}"")'>
                                <span class='toggle-icon'>▶</span>
                                <span class='code-label'>{section.Language.ToUpper()} Code</span>
                                <span class='toggle-text'>Click to expand</span>
                            </button>
                            <div class='code-block collapsed' id='{codeId}'>
                                <div class='code-header'>
                                    <span class='language-tag'>{section.Language.ToUpper()}</span>
                                    <button class='copy-code-btn' onclick='copyCodeToClipboard(""{codeId}"")'>Copy</button>
                                </div>
                                <pre class='code-content'><code>{escapedCode}</code></pre>
                            </div>
                        </div>");
                    }
                    else
                    {
                        // Process normal text with markdown
                        string htmlText = _markdownProcessor.Transform(section.Content);
                        htmlBody.AppendLine(htmlText);
                    }
                }

                htmlContent = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            margin: 12px;
            color: #333;
            background-color: white;
        }}
        
        h1, h2, h3, h4, h5, h6 {{
            color: #2C3E50;
            margin-top: 20px;
            margin-bottom: 10px;
        }}
        
        p {{ margin-bottom: 16px; }}
        
        code {{
            background-color: #f6f8fa;
            border: 1px solid #e1e4e8;
            padding: 2px 6px;
            border-radius: 3px;
            font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
            font-size: 0.9em;
            color: #d73a49;
        }}
        
        .code-container {{
            margin: 20px 0;
            border: 1px solid #e1e4e8;
            border-radius: 8px;
            overflow: hidden;
            background-color: #f8f9fa;
        }}
        
        .code-toggle-btn {{
            width: 100%;
            padding: 12px 16px;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            border: none;
            cursor: pointer;
            font-size: 14px;
            font-weight: 600;
            display: flex;
            align-items: center;
            text-align: left;
            gap: 8px;
            transition: all 0.3s ease;
        }}
        
        .code-toggle-btn:hover {{
            background: linear-gradient(135deg, #5a67d8 0%, #6b46c1 100%);
        }}
        
        .toggle-icon {{
            font-size: 12px;
            transition: transform 0.3s ease;
            display: inline-block;
        }}
        
        .code-toggle-btn.expanded .toggle-icon {{
            transform: rotate(90deg);
        }}
        
        .code-label {{
            font-weight: bold;
        }}
        
        .toggle-text {{
            margin-left: auto;
            font-size: 12px;
            opacity: 0.9;
        }}
        
        .code-block {{
            background-color: #f8f9fa;
            transition: all 0.3s ease;
            overflow: hidden;
            max-height: 0;
            opacity: 0;
        }}
        
        .code-block.collapsed {{
            max-height: 0;
            opacity: 0;
        }}
        
        .code-block.expanded {{
            max-height: 2000px;
            opacity: 1;
        }}
        
        .code-header {{
            background-color: #e9ecef;
            padding: 8px 16px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            border-bottom: 1px solid #dee2e6;
        }}
        
        .language-tag {{
            background-color: #0e639c;
            color: white;
            padding: 2px 8px;
            border-radius: 3px;
            font-size: 11px;
            font-weight: bold;
        }}
        
        .copy-code-btn {{
            background-color: #6c757d;
            color: white;
            border: none;
            padding: 4px 8px;
            border-radius: 4px;
            cursor: pointer;
            font-size: 11px;
            transition: all 0.2s ease;
        }}
        
        .copy-code-btn:hover {{
            background-color: #5a6268;
        }}
        
        .copy-code-btn:active {{
            background-color: #28a745;
            transform: scale(0.95);
        }}
        
        .code-content {{
            margin: 0;
            padding: 16px;
            font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
            font-size: 13px;
            line-height: 1.4;
            overflow-x: auto;
            white-space: pre;
            background-color: #ffffff;
            color: #333333;
            border: none;
        }}
        
        .code-content code {{
            background: transparent;
            border: none;
            padding: 0;
            color: inherit;
            font-size: inherit;
            white-space: pre;
            font-family: inherit;
        }}
        
        blockquote {{
            border-left: 4px solid #dfe2e5;
            margin: 0;
            padding: 0 16px;
            color: #6a737d;
        }}
        
        ul, ol {{ padding-left: 30px; }}
        li {{ margin-bottom: 4px; }}
        strong {{ color: #24292e; font-weight: 600; }}
        em {{ font-style: italic; }}
        
        table {{
            border-collapse: collapse;
            width: 100%;
            margin: 16px 0;
        }}
        
        th, td {{
            border: 1px solid #dfe2e5;
            padding: 8px 13px;
            text-align: left;
        }}
        
        th {{
            background-color: #f6f8fa;
            font-weight: 600;
        }}
        
        hr {{
            border: none;
            height: 1px;
            background-color: #e1e4e8;
            margin: 24px 0;
        }}
    </style>
    <script>
    if (typeof console === ""undefined"") {{
        console = {{ log: function () {{ }}, error: function () {{ }} }};
    }}

    function toggleCodeBlock(codeId, buttonId) {{
        try {{
            var codeBlock = document.getElementById(codeId);
            var button = document.getElementById(buttonId);
            var toggleText = button.querySelector('.toggle-text');
            
            if (codeBlock && button && toggleText) {{
                if (codeBlock.classList.contains('collapsed')) {{
                    codeBlock.classList.remove('collapsed');
                    codeBlock.classList.add('expanded');
                    button.classList.add('expanded');
                    toggleText.textContent = 'Click to collapse';
                }} else {{
                    codeBlock.classList.remove('expanded');
                    codeBlock.classList.add('collapsed');
                    button.classList.remove('expanded');
                    toggleText.textContent = 'Click to expand';
                }}
            }}
        }} catch (e) {{
            console.error('Error toggling code block:', e);
        }}
    }}

    function copyCodeToClipboard(codeId) {{
        try {{
            var codeElement = document.getElementById(codeId);
            if (codeElement) {{
                var codeContentElement = codeElement.querySelector('.code-content code');
                if (codeContentElement) {{
                    var codeText = codeContentElement.textContent || codeContentElement.innerText;
                    
                    if (navigator.clipboard && navigator.clipboard.writeText) {{
                        navigator.clipboard.writeText(codeText).then(function () {{
                            console.log('Code copied to clipboard');
                            // Visual feedback
                            var copyBtn = codeElement.querySelector('.copy-code-btn');
                            if (copyBtn) {{
                                var originalText = copyBtn.textContent;
                                copyBtn.textContent = 'Copied!';
                                copyBtn.style.backgroundColor = '#28a745';
                                setTimeout(function () {{
                                    copyBtn.textContent = originalText;
                                    copyBtn.style.backgroundColor = '';
                                }}, 1500);
                            }}
                        }}).catch(function (err) {{
                            console.error('Failed to copy code: ', err);
                            fallbackCopyToClipboard(codeText);
                        }});
                    }} else {{
                        fallbackCopyToClipboard(codeText);
                    }}
                }}
            }}
        }} catch (e) {{
            console.error('Error copying code:', e);
        }}
    }}

    function fallbackCopyToClipboard(text) {{
        try {{
            var textArea = document.createElement('textarea');
            textArea.value = text;
            textArea.style.position = 'fixed';
            textArea.style.left = '-999999px';
            textArea.style.top = '-999999px';
            document.body.appendChild(textArea);
            textArea.focus();
            textArea.select();
            
            var successful = document.execCommand('copy');
            document.body.removeChild(textArea);
            
            if (successful) {{
                console.log('Code copied using fallback method');
            }} else {{
                console.error('Fallback copy failed');
            }}
        }} catch (err) {{
            console.error('Fallback copy error:', err);
        }}
    }}

</script>
</head>
<body>
    {htmlBody}
</body>
</html>";
            }
            else
            {
                htmlContent = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            margin: 12px;
            color: #666;
            background-color: white;
        }}
    </style>
</head>
<body>
    <p>{System.Web.HttpUtility.HtmlEncode(content).Replace("\n", "<br>")}</p>
</body>
</html>";
            }

            webBrowser.NavigateToString(htmlContent);
        }

        private void CopyResponseToClipboard()
        {
            try
            {
                if (!string.IsNullOrEmpty(_lastResponse))
                {
                    System.Windows.Clipboard.SetText(_lastResponse);
                    System.Windows.MessageBox.Show("Response copied to clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show("No response to copy!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveResponseToFile()
        {
            try
            {
                if (string.IsNullOrEmpty(_lastResponse))
                {
                    System.Windows.MessageBox.Show("No response to save!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var saveFileDialog = new WinFormsSaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    DefaultExt = "txt",
                    FileName = $"AI_Analysis_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveFileDialog.ShowDialog() == WinFormsDialogResult.OK)
                {
                    File.WriteAllText(saveFileDialog.FileName, _lastResponse);
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
