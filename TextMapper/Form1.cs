using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TextMapper
{
    public partial class Form1 : Form
    {
        private static readonly JsonSerializerOptions JsonWriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        JsonObject EditingTranslationObj { get; set; } = new JsonObject();

        [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
        public static extern short GetKeyState(int nVirtKey);
        public const int VK_LCONTROL = 0xA2;
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            var OFD = new OpenFileDialog();
            OFD.Filter = "Json Files|*.json";
            OFD.Title = "Select the Json file to load";
            OFD.Multiselect = false;
            if (OFD.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            this.Text = Path.GetFileName(OFD.FileName);
            var JsonStr = File.ReadAllText(OFD.FileName);
            this.EditingTranslationObj = JsonNode.Parse(JsonStr)!.AsObject();
            this.TextList.Items.Clear();
            RefreshTextList();
        }

        void RefreshTextList()
        {
            var Messages = EditingTranslationObj["MESSAGE"] as JsonArray;
            if (Messages != null)
            {
                while(Messages.Count > this.TextList.Items.Count)
                {
                    this.TextList.Items.Add("");
                }
                for (int i = 0; i < Messages.Count; i++)
                {
                    var message = Messages[i]!.AsObject();
                    this.TextList.Items[i] = message["EN"]?.GetValue<string>() + "/" +
                                             message["Translation"]?.GetValue<string>();
                }
            }
        }

        private void TextList_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (TextList.SelectedIndex < 0)
                {
                    return;
                }
                var Messages = EditingTranslationObj["MESSAGE"] as JsonArray;
                if (Messages == null)
                {
                    return;
                }
                var TrasnlateTextObj = Messages[TextList.SelectedIndex] as JsonObject;
                if (TrasnlateTextObj != null)
                {
                    textJP.Text = TrasnlateTextObj["JP"]?.ToString();
                    textEN.Text = TrasnlateTextObj["EN"]?.ToString();
                    textTranslation.Text = TrasnlateTextObj["Translation"]?.ToString();
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                this.contextMenuStrip1.Show(this.TextList, e.Location);
            }
        }

        private void InsertClipboard_Click(object sender, EventArgs e)
        {
            if (TextList.SelectedIndex < 0)
            {
                return;
            }
            string TextPendingInsert = Clipboard.GetText().Replace("\r", "");
            string[] TextLines = TextPendingInsert.Split('\n');
            var Messages = EditingTranslationObj["MESSAGE"] as JsonArray;
            if(Messages == null)
            {
                return;
            }
            for (int i=0;i<TextLines.Length;i++)
            {
                if(i+TextList.SelectedIndex>= Messages.Count)
                {
                    break;
                }
                var TrasnlateTextObj = Messages[i + TextList.SelectedIndex] as JsonObject;
                if(TrasnlateTextObj != null)
                {
                    string PendingReplace = TextLines[i];
                    if ((GetKeyState(VK_LCONTROL) & 0x8000) == 0)
                    {
                        if (TextLines[i].Contains("「") && TextLines[i].IndexOf('「') != 0 && !TextLines[i].StartsWith("`"))
                        {
                            int DialogueStart = TextLines[i].IndexOf('「');
                            if (DialogueStart < 5)
                            {
                                PendingReplace = "`" + PendingReplace.Insert(DialogueStart, "@");
                            }
                        }
                        else if (TextLines[i].Contains("『") && TextLines[i].IndexOf('『') != 0 && !TextLines[i].StartsWith("`"))
                        {
                            int DialogueStart = TextLines[i].IndexOf('『');
                            if (DialogueStart < 5)
                            {
                                PendingReplace = "`" + PendingReplace.Insert(DialogueStart, "@");
                            }
                        }
                        else if (TextLines[i].Contains("（") && TextLines[i].IndexOf('（') != 0 && !TextLines[i].StartsWith("`"))
                        {
                            int DialogueStart = TextLines[i].IndexOf('（');
                            if (DialogueStart < 4)
                            {
                                PendingReplace = "`" + PendingReplace.Insert(DialogueStart, "@");
                            }
                        }
                    }
                    TrasnlateTextObj["Translation"] = PendingReplace;
                }
            }
            RefreshTextList();
        }

        private void InsertText_Click(object sender, EventArgs e)
        {
            if (TextList.SelectedIndex < 0)
            {
                return;
            }
            var Messages = EditingTranslationObj["MESSAGE"] as JsonArray;
            if (Messages == null)
            {
                return;
            }
            for (int i = Messages.Count - 1; i > TextList.SelectedIndex; i--)
            {
                var TrasnlateTextBObj = Messages[i] as JsonObject;
                var TrasnlateTextAObj = Messages[i-1] as JsonObject;
                if (TrasnlateTextBObj != null && TrasnlateTextAObj != null)
                {
                    TrasnlateTextBObj["Translation"] = TrasnlateTextAObj["Translation"]?.DeepClone();
                }
            }
            RefreshTextList();
        }

        private void RemoveText_Click(object sender, EventArgs e)
        {
            if (TextList.SelectedIndex < 0)
            {
                return;
            }
            var Messages = EditingTranslationObj["MESSAGE"] as JsonArray;
            if (Messages == null)
            {
                return;
            }
            for (int i = TextList.SelectedIndex;i<Messages.Count-1; i++)
            {
                var TrasnlateTextAObj = Messages[i] as JsonObject;
                var TrasnlateTextBObj = Messages[i + 1] as JsonObject;
                if (TrasnlateTextBObj != null && TrasnlateTextAObj != null)
                {
                    TrasnlateTextAObj["Translation"] = TrasnlateTextBObj["Translation"]?.DeepClone();
                }
            }
            RefreshTextList();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            var OFD = new SaveFileDialog();
            OFD.Filter = "Json Files|*.json";
            OFD.Title = "Select the Json file to load";
            if (OFD.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            File.WriteAllText(OFD.FileName, EditingTranslationObj.ToJsonString(JsonWriteOptions));
        }

        private void button3_Click(object sender, EventArgs e)
        {
            MessageBox.Show("这是一个很简单的小工具，用于快速替换翻译文本。\n由于翻译和文本很大部分都是对应的，所以填起来应该很快。\n"+
            "右键菜单:\n"+
            "1.插入剪贴板内容：会将剪贴板里的文本按行分割填入，会覆盖现有文本，可以一次填很多行\n"+
            "2.插入文本：在当前位置插入一行文本，剩下文本整体后移动一行\n"+
            "3.移除文本：移除当前文本，剩下文本整体向前移动一行\n"+
            "如果遇见插入文本后错位，可以通过插入和移除错位的行，一般移除几条后其他大部分翻译就会对得很齐了。\n"+
            "带有『和「的文本会被自动加上说话人的标记（`和@），如果有意外添加的标记，需要手动删除");
        }

        private void textTranslation_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.KeyCode == Keys.Enter)
            {
                if (TextList.SelectedIndex < 0)
                {
                    return;
                }
                var Messages = EditingTranslationObj["MESSAGE"] as JsonArray;
                if (Messages == null)
                {
                    return;
                }
                var TrasnlateTextObj = Messages[TextList.SelectedIndex] as JsonObject;
                if (TrasnlateTextObj != null)
                {
                    TrasnlateTextObj["Translation"] = this.textTranslation.Text;
                }
                RefreshTextList();
            }
        }
    }
}
