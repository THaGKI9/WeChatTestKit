using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml;

namespace WeChatTestKit
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Regex regexToken = new Regex("^[a-zA-Z0-9]{3,32}$", RegexOptions.Compiled);
        private Regex regexKey = new Regex("^[a-zA-Z0-9]{43}$", RegexOptions.Compiled);
        private bool ifAESValidated = false;
        private bool checkEncryption = false;
        private Tencent.WXBizMsgCrypt cryptor;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            textBox_Url.Text = ConfigurationManager.AppSettings["apiUrl"];
            textBox_token.Text = ConfigurationManager.AppSettings["token"];
            textBox_AESKey.Text = ConfigurationManager.AppSettings["AESKey"];
            checkBox_enableEncryption.IsChecked = ConfigurationManager.AppSettings["enableEncryption"] == "Enable";
            textBox_AESKey.IsEnabled = ConfigurationManager.AppSettings["enableEncryption"] == "Enable";

            cryptor = new Tencent.WXBizMsgCrypt(textBox_token.Text, textBox_AESKey.Text, "");
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            WriteAppConfig(config, "apiUrl", textBox_Url.Text);
            WriteAppConfig(config, "token", textBox_token.Text);
            WriteAppConfig(config, "AESKey", textBox_AESKey.Text);
            WriteAppConfig(config, "enableEncryption", checkBox_enableEncryption.IsChecked == true ? "Enable" : "Disable");
            config.Save(ConfigurationSaveMode.Modified);
        }

        private async void button_verifyInterface_Click(object sender, RoutedEventArgs e)
        {   
            textBox_Result.Text = "正在验证...\r\n\r\n";
            textBox_ResultRaw.Text = "";
            Grid_Functions.IsEnabled = false;
            label_Timeout.Visibility = Visibility.Hidden;

            var parameters = GenerateParams();
            var echostr = parameters["echostr"] = RandomString(19);
            var api_uri = GetUrlWithQuery(parameters);
            if (api_uri == string.Empty)
            {
                textBox_Result.Text = "接口地址格式错误，请修改！";
                return;
            }

            var webRequest = WebRequest.Create(api_uri);
            webRequest.Timeout = 5000;

            bool isValidated = false;
            var result = string.Empty;
            var rawResult = string.Empty;

            try
            {
                var response = await webRequest.GetResponseAsync();
                using (var receiveStream = response.GetResponseStream())
                using (var reader = new StreamReader(receiveStream, Encoding.UTF8))
                {
                    rawResult = reader.ReadToEnd();
                    isValidated = rawResult.Equals(echostr);

                    if (!isValidated) result = "返回的echostr不匹配";
                }
                response.Close();
            }
            catch (WebException ex)
            {
                var response = (HttpWebResponse)ex.Response;
                switch (ex.Status)
                {
                    case WebExceptionStatus.ProtocolError:
                        result = $"HTTP请求错误\r\n状态码：{Convert.ToInt32(response.StatusCode)} - {response.StatusDescription}";
                        break;
                    case WebExceptionStatus.Timeout:
                        result = "请求超时";
                        break;
                    default:
                        result = "WebRequest发生异常，异常信息：" + ex.Message;
                        break;
                }
                rawResult = ex.ToString();
            }
            catch (Exception ex)
            {
                result = $"发生异常\r\n异常类型：{ex.GetType().ToString()}\r\n异常信息：{ex.Message}";
            }
            finally
            {
                result = (isValidated ? "验证通过\r\n" : "验证失败\r\n") + result;
            }

            textBox_Result.Text = result;
            textBox_ResultRaw.Text = rawResult;
            Grid_Functions.IsEnabled = true;

        }

        private async void button_send_Click(object sender, RoutedEventArgs e)
        {
            textBox_Result.Text = "正在发送...\r\n\r\n";
            textBox_ResultRaw.Text = "";
            Grid_Functions.IsEnabled = false;
            label_Timeout.Visibility = Visibility.Hidden;

            bool ifSendRaw = sender == button_SendXML;
            var parameters = GenerateParams();
            var apiUrl = string.Empty;
            var xmlString = GenerateXMLString(ifSendRaw);
            if (ifAESValidated)
            {
                var ret = cryptor.EncryptMsg(xmlString, parameters["timestamp"], parameters["nonce"], ref xmlString);
                apiUrl = GetUrlWithQuery(parameters, encryptedMsg: xmlString);
            }  
            else apiUrl = GetUrlWithQuery(parameters);
            
            if (apiUrl == string.Empty)
            {
                textBox_Result.Text = "接口地址格式错误，请修改！";
                return;
            }

            var buffer = Encoding.UTF8.GetBytes(xmlString);
            var result = string.Empty;
            var rawResult = string.Empty;

            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            var webRequest = WebRequest.Create(apiUrl);
            webRequest.Method = "POST";
            webRequest.ContentType = "application/xml, charset=UTF-8";
            webRequest.ContentLength = buffer.Length;
            webRequest.Timeout = 10000;

            try
            {
                using (var stream = await webRequest.GetRequestStreamAsync())
                    stream.Write(buffer, 0, buffer.Length);

                var response = await webRequest.GetResponseAsync();
                using (var receiveStream = response.GetResponseStream())
                using (var reader = new StreamReader(receiveStream, Encoding.UTF8))
                {
                    rawResult = reader.ReadToEnd();
                    if (rawResult == "success" || rawResult == "") result = "服务器返回空字符串或者success";
                    else
                    {
                        XmlDocument xmlDoc = new XmlDocument();
                            
                        xmlDoc.LoadXml(rawResult);
                        XmlNode msgTypeNode = xmlDoc.SelectSingleNode("/xml/MsgType");
                        result = (msgTypeNode == null) ? "返回的XML结构不完整，缺失MsgTypeTime节点" : FormatMessage(xmlDoc, msgTypeNode);
                        
                    }
                }
                response.Close();
            }
            catch (XmlException)
            {
                result = "解析XML发生异常，请查看源数据以调试";
            }
            catch (Exception ex)
            {
                result = $"发生异常\r\n异常类型：{ex.GetType().ToString()}\r\n异常信息：{ex.Message}";
            }

            textBox_Result.Text = result;
            textBox_ResultRaw.Text = rawResult;
            Grid_Functions.IsEnabled = true;

            stopWatch.Stop();
            if (stopWatch.Elapsed.TotalSeconds > 5) label_Timeout.Visibility = Visibility.Visible;
        }

        private void label_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Process.Start("https://github.com/THaGKI9/WeChatTestKit");
        }

        private void checkBox_ShowRawResult_Click(object sender, RoutedEventArgs e)
        {
            textBox_Result.Visibility = checkBox_ShowRawResult.IsChecked == true ? Visibility.Hidden : Visibility.Visible;
            textBox_ResultRaw.Visibility = checkBox_ShowRawResult.IsChecked == false ? Visibility.Hidden : Visibility.Visible;
        }

        private void checkBox_enableEncryption_Click(object sender, RoutedEventArgs e)
        {
            checkEncryption = checkBox_enableEncryption.IsChecked == true;
            textBox_AESKey.IsEnabled = checkBox_enableEncryption.IsChecked == true;
        }

        private void textBox_token_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            textBox_token.Background = regexToken.IsMatch(textBox_token.Text) ? Brushes.White : Brushes.Yellow;
        }

        private void textBox_AESKey_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            bool isMatch = regexKey.IsMatch(textBox_AESKey.Text);
            bool isEmpty = textBox_AESKey.Text == "";
            textBox_AESKey.Background = isEmpty || isMatch ? Brushes.White : Brushes.Yellow;
            ifAESValidated = isMatch && !isEmpty;
        }

        Dictionary<string, string> GenerateParams()
        {
            return new Dictionary<string, string>
            {
                { "timestamp", GetTimestamp() },
                { "nonce", RandomString(10) },
                { "token", textBox_token.Text }
            };
        }

        string GetUrlWithQuery(Dictionary<string, string> parameters, string encryptedMsg = null)
        {
            try
            {
                var timestamp = parameters["timestamp"];
                var nonce = parameters["nonce"];
                var uriBuilder = new UriBuilder(textBox_Url.Text.Trim());
                var query = string.Empty;

                if (parameters.ContainsKey("echostr"))
                {
                    List<string> list = new List<string> { timestamp, nonce, textBox_token.Text };
                    string signature = GenerateSignature(list);
                    query = $"signature={signature}&echostr={parameters["echostr"]}";
                }
                
                if (encryptedMsg != null)
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.LoadXml(encryptedMsg);

                    var cipher_text_base64 = xmlDoc.SelectSingleNode("/xml/Encrypt").InnerText;
                    List<string> list = new List<string> { timestamp, nonce, textBox_token.Text, cipher_text_base64 };
                    query += "encrypt_type=aes&msg_signature=" + GenerateSignature(list);
                }

                uriBuilder.Query = query + $"&timestamp={timestamp}&nonce={nonce}";
                return uriBuilder.ToString();
            }
            catch (UriFormatException) { return ""; }
        }
        
        static string GenerateSignature(List<string> list)
        {
            list.Sort(string.CompareOrdinal);
            var raw = string.Concat(list);

            SHA1 sha1 = new SHA1CryptoServiceProvider();
            byte[] resultBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(raw));
            var signature = BitConverter.ToString(resultBytes).Replace("-", "").ToLower();
            return signature;
        }

        static string GetTimestamp()
        {
            long ts = (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            return ts.ToString();
        }

        static string RandomString(int length, string seed = "0123456789")
        {
            var random = new Random();
            var stringBuilder = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                stringBuilder.Append(seed[random.Next(seed.Length)]);
            return stringBuilder.ToString();
        }

        private string GenerateXMLString(bool ifEncrypt)
        {
            if (ifEncrypt) return textBox_Payload.Text;

            var toUserName = "WeChatTestKitClient";
            var fromUserName = "WeChatTestKitServer";
            var createTime = GetTimestamp();
            var msgType = "text";
            var content = textBox_Payload.Text;
            var msgId = "MsgId" + createTime;

            XmlDocument xmlDoc = new XmlDocument();

            XmlNode rootNode = xmlDoc.CreateElement("xml");
            xmlDoc.AppendChild(rootNode);

            XmlNode toUserNode = xmlDoc.CreateElement("ToUserName");
            toUserNode.AppendChild(xmlDoc.CreateCDataSection(toUserName));
            rootNode.AppendChild(toUserNode);

            XmlNode fromUserNode = xmlDoc.CreateElement("FromUserName");
            fromUserNode.AppendChild(xmlDoc.CreateCDataSection(fromUserName));
            rootNode.AppendChild(fromUserNode);

            XmlNode createTimeNode = xmlDoc.CreateElement("CreateTime");
            createTimeNode.AppendChild(xmlDoc.CreateTextNode(createTime));
            rootNode.AppendChild(createTimeNode);

            XmlNode msgTypeNode = xmlDoc.CreateElement("MsgType");
            msgTypeNode.AppendChild(xmlDoc.CreateCDataSection(msgType));
            rootNode.AppendChild(msgTypeNode);

            XmlNode contentNode = xmlDoc.CreateElement("Content");
            contentNode.AppendChild(xmlDoc.CreateCDataSection(content));
            rootNode.AppendChild(contentNode);

            XmlNode msgIdNode = xmlDoc.CreateElement("MsgId");
            msgIdNode.AppendChild(xmlDoc.CreateTextNode(msgId));
            rootNode.AppendChild(msgIdNode);

            return xmlDoc.InnerXml;
        }

        static string FormatMessage(XmlDocument xmlDoc, XmlNode msgTypeNode)
        {
            string result = "收到信息，类型：" + msgTypeNode.InnerText + "\r\n\r\n";
            switch (msgTypeNode.InnerText)
            {
                case "text":
                    result += "Content:\r\n" + xmlDoc.SelectSingleNode("/xml/Content")?.InnerText;
                    break;
                case "image":
                    result += "MediaId: " + xmlDoc.SelectSingleNode("/xml/Image/MediaId")?.InnerText;
                    break;
                case "voice":
                    result += "MediaId: " + xmlDoc.SelectSingleNode("/xml/Voice/MediaId")?.InnerText;
                    break;
                case "video":
                    result += string.Format("MediaId: {0}\r\nTitle: {1}\r\nDescription: {2}",
                        xmlDoc.SelectSingleNode("/xml/Video/MediaId")?.InnerText,
                        xmlDoc.SelectSingleNode("/xml/Video/Title")?.InnerText,
                        xmlDoc.SelectSingleNode("/xml/Video/Description")?.InnerText);
                    break;
                case "music":
                    result += string.Format("Title: {0}\r\nDescription: {1}\r\nMusicUrl: {2}\r\nHQMusicUrl: {3}\r\nThumbMediaId: {4}",
                        xmlDoc.SelectSingleNode("/xml/Music/Title")?.InnerText,
                        xmlDoc.SelectSingleNode("/xml/Music/Description")?.InnerText,
                        xmlDoc.SelectSingleNode("/xml/Music/MusicUrl")?.InnerText,
                        xmlDoc.SelectSingleNode("/xml/Music/HQMusicUrl")?.InnerText,
                        xmlDoc.SelectSingleNode("/xml/Music/ThumbMediaId")?.InnerText);
                    break;
                case "":
                    result += $"ArticleCount: {xmlDoc.SelectSingleNode("/xml/ArticleCount")?.InnerText}\r\n";

                    int index = 1;
                    foreach (XmlNode node in xmlDoc.SelectNodes("/xml/Articles/item"))
                    {
                        result += string.Format("Article #{0}\r\nTitle: {1}\r\nDescription: {2}\r\nPicUrl: {3}\r\nUrl: {4}\r\n",
                            index,
                            node.SelectSingleNode("./Title")?.InnerText,
                            node.SelectSingleNode("./Description")?.InnerText,
                            node.SelectSingleNode("./PicUrl")?.InnerText,
                            node.SelectSingleNode("./Url")?.InnerText);
                    }
                    break;
            }
            return result;
        }

        private void WriteAppConfig(Configuration config, string key, string value)
        {
            KeyValueConfigurationElement keyValueElement = config.AppSettings.Settings[key];
            if (keyValueElement == null) config.AppSettings.Settings.Add(key, value);
            else keyValueElement.Value = value;
        }
    }
}
