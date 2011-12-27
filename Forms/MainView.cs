using System;
using System.Windows.Forms;
using System.Collections.Generic;

namespace TESsnip
{
    internal delegate string dFormIDLookupS(string id);
    internal delegate string dFormIDLookupI(uint id);
    internal delegate string dLStringLookup(uint id);
    internal delegate string[] dFormIDScan(string type);
    internal partial class MainView : Form
    {
        public static BaseRecord Clipboard;
        public static TreeNode ClipboardNode;
        private bool SelectedSubrecord;
        private Record parentRecord;
        private SearchForm searchForm;
        //private SubrecordStructure[] SubrecordStructs;
        private Plugin[] FormIDLookup;
        private uint[] Fixups;
        private Forms.StringsEditor stringEditor = null;

        public MainView()
        {
            if (!RecordStructure.Loaded)
            {
                try
                {
                    RecordStructure.Load();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not parse RecordStructure.xml. Record-at-once editing will be unavailable.\n" + ex.Message, "Warning");
                }
            }
            InitializeComponent();

            this.listSubrecord.Columns.Clear();
            this.listSubrecord.AddBindingColumn("Name", "Name", 40);
            this.listSubrecord.AddBindingColumn("Size", "Size", 40);
            this.listSubrecord.AddBindingColumn("IsValid", "*", 20, new Func<SubRecord, string>(a => a.IsValid ? "*" : ""));
            this.listSubrecord.AddBindingColumn("Description", "Description", 80);
            
            this.SaveModDialog.InitialDirectory = Program.gameDataDir;
            this.OpenModDialog.InitialDirectory = Program.gameDataDir;

            this.Icon = Properties.Resources.tesv_ico;
            Settings.GetWindowPosition("TESsnip", this);
        }

        internal void LoadPlugin(string s)
        {
            Plugin p = new Plugin(s, false);
            TreeNode tn = new TreeNode(p.Name);
            CreatePluginTree(p, tn);
            PluginTree.Nodes.Add(tn);
            UpdateStringEditor();
        }

        private void WalkPluginTree(Rec r, TreeNode tn)
        {
            TreeNode tn2 = new TreeNode(r.DescriptiveName);
            tn2.Tag = r;
            if (r is GroupRecord)
            {
                foreach (Rec r2 in ((GroupRecord)r).Records) WalkPluginTree(r2, tn2);
            }
            tn.Nodes.Add(tn2);
        }

        private void CreatePluginTree(Plugin p, TreeNode tn)
        {
            tn.Tag = p;
            foreach (Rec r in p.Records) WalkPluginTree(r, tn);
        }

        private void openNewPluginToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ValidateMakeChange())
                return;
            if (OpenModDialog.ShowDialog() == DialogResult.OK)
            {
                foreach (string s in OpenModDialog.FileNames)
                {
                    LoadPlugin(s);
                }
            }
        }

        private void closeAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ValidateMakeChange())
                return;
            if (MessageBox.Show("This will close all open plugins, and you will lose any unsaved changes.\n" +
                "Are you sure you wish to continue", "Warning", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            PluginTree.Nodes.Clear();
            Clipboard = null;
            ClipboardNode = null;
            CloseStringEditor();
            GC.Collect();
        }

        private struct LoopBlock
        {
            public readonly int start;
            public readonly int end;

            public LoopBlock(int start, int end)
            {
                this.start = start;
                this.end = end;
            }
        }
        private struct Conditional
        {
            public readonly ElementValueType type;
            public readonly object value;

            public Conditional(ElementValueType type, object value)
            {
                this.type = type;
                this.value = value;
            }
        }
        private static void MatchRecordAddConditionals(Dictionary<int, Conditional> conditions, SubRecord sr, ElementStructure[] ess)
        {
            int offset = 0;
            byte[] data = sr.GetReadonlyData();
            for (int j = 0; j < ess.Length; j++)
            {
                if (ess[j].CondID != 0)
                {
                    switch (ess[j].type)
                    {
                        case ElementValueType.Int:
                        case ElementValueType.FormID:
                            conditions[ess[j].CondID] = new Conditional(ElementValueType.Int, TypeConverter.h2si(data[offset], data[offset + 1], data[offset + 2], data[offset + 3]));
                            offset += 4;
                            break;
                        case ElementValueType.UInt:
                            conditions[ess[j].CondID] = new Conditional(ElementValueType.UInt, (uint)TypeConverter.h2i(data[offset], data[offset + 1], data[offset + 2], data[offset + 3]));
                            offset += 4;
                            break;
                        case ElementValueType.Float:
                            conditions[ess[j].CondID] = new Conditional(ElementValueType.Float, TypeConverter.h2f(data[offset], data[offset + 1], data[offset + 2], data[offset + 3]));
                            offset += 4;
                            break;
                        case ElementValueType.Short:
                            conditions[ess[j].CondID] = new Conditional(ElementValueType.Short, (int)TypeConverter.h2ss(data[offset], data[offset + 1]));
                            offset += 2;
                            break;
                        case ElementValueType.UShort:
                            conditions[ess[j].CondID] = new Conditional(ElementValueType.UShort, TypeConverter.h2s(data[offset], data[offset + 1]));
                            offset += 2;
                            break;
                        case ElementValueType.SByte:
                            conditions[ess[j].CondID] = new Conditional(ElementValueType.SByte, (sbyte)data[offset]);
                            offset++;
                            break;
                        case ElementValueType.Byte:
                            conditions[ess[j].CondID] = new Conditional(ElementValueType.Byte, (int)data[offset]);
                            offset++;
                            break;
                        case ElementValueType.String:
                            {
                                string s = "";
                                while (data[offset] != 0) s += (char)data[offset++];
                                offset++;
                                conditions[ess[j].CondID] = new Conditional(ElementValueType.String, s);
                            }
                            break;
                        case ElementValueType.fstring:
                            conditions[ess[j].CondID] = new Conditional(ElementValueType.String, sr.GetStrData());
                            break;
                        case ElementValueType.BString:
                            {
                                int len = TypeConverter.h2s(data[offset], data[offset + 1]);
                                string s = System.Text.Encoding.ASCII.GetString(data, offset + 2, len);
                                offset = offset + (2 + len + 1);
                                conditions[ess[j].CondID] = new Conditional(ElementValueType.String, s);
                            } break;
                        case ElementValueType.LString:
                            conditions[ess[j].CondID] = new Conditional(ElementValueType.Int, TypeConverter.h2si(data[offset], data[offset + 1], data[offset + 2], data[offset + 3]));
                            offset += 4;
                            break;
                        default:
                            throw new ApplicationException();
                    }
                }
                else
                {
                    switch (ess[j].type)
                    {
                        case ElementValueType.Int:
                        case ElementValueType.UInt:
                        case ElementValueType.FormID:
                        case ElementValueType.Float:
                            offset += 4;
                            break;
                        case ElementValueType.UShort:
                        case ElementValueType.Short:
                            offset += 2;
                            break;
                        case ElementValueType.SByte:
                        case ElementValueType.Byte:
                            offset++;
                            break;
                        case ElementValueType.String:
                            while (data[offset] != 0) offset++;
                            offset++;
                            break;
                        case ElementValueType.fstring:
                            break;
                        case ElementValueType.LString:
                            break;
                        case ElementValueType.BString:
                            int len = TypeConverter.h2s(data[offset], data[offset + 1]);
                            offset += 2 + len + 1;
                            break;
                        default:
                            throw new ApplicationException();
                    }
                }
            }
        }
        private static bool MatchRecordCheckCondition(Dictionary<int, Conditional> conditions, SubrecordStructure ss)
        {
            if (ss.Condition == CondType.Exists)
            {
                if (conditions.ContainsKey(ss.CondID)) return true;
                else return false;
            }
            else if (ss.Condition == CondType.Missing)
            {
                if (conditions.ContainsKey(ss.CondID)) return false;
                else return true;
            }
            if (!conditions.ContainsKey(ss.CondID)) return false;
            Conditional cond = conditions[ss.CondID];
            switch (cond.type)
            {
                case ElementValueType.SByte:
                case ElementValueType.Byte:
                case ElementValueType.UShort:
                case ElementValueType.Short:
                case ElementValueType.Int:
                case ElementValueType.UInt:
                case ElementValueType.FormID:
                    {
                        int i = Convert.ToInt32(cond.value), i2;
                        if (!int.TryParse(ss.CondOperand, out i2)) return false;
                        switch (ss.Condition)
                        {
                            case CondType.Equal: return i == i2;
                            case CondType.Not: return i != i2;
                            case CondType.Less: return i < i2;
                            case CondType.Greater: return i > i2;
                            case CondType.GreaterEqual: return i >= i2;
                            case CondType.LessEqual: return i <= i2;
                            default: return false;
                        }
                    }
                case ElementValueType.Float:
                    {
                        float i = (float)cond.value, i2;
                        if (!float.TryParse(ss.CondOperand, out i2)) return false;
                        switch (ss.Condition)
                        {
                            case CondType.Equal: return i == i2;
                            case CondType.Not: return i != i2;
                            case CondType.Less: return i < i2;
                            case CondType.Greater: return i > i2;
                            case CondType.GreaterEqual: return i >= i2;
                            case CondType.LessEqual: return i <= i2;
                            default: return false;
                        }
                    }
                case ElementValueType.fstring:
                case ElementValueType.BString:
                case ElementValueType.String:
                    {
                        string s = (string)cond.value;
                        switch (ss.Condition)
                        {
                            case CondType.Equal: return s == ss.CondOperand;
                            case CondType.Not: return s != ss.CondOperand;
                            case CondType.StartsWith: return s.StartsWith(ss.CondOperand);
                            case CondType.EndsWith: return s.EndsWith(ss.CondOperand);
                            case CondType.Contains: return s.Contains(ss.CondOperand);
                            default: return false;
                        }
                    }
                case ElementValueType.LString:
                    {
                        int i = (int)cond.value, i2;
                        if (!int.TryParse(ss.CondOperand, out i2)) return false;
                        switch (ss.Condition)
                        {
                            case CondType.Equal: return i == i2;
                            case CondType.Not: return i != i2;
                            case CondType.Less: return i < i2;
                            case CondType.Greater: return i > i2;
                            case CondType.GreaterEqual: return i >= i2;
                            case CondType.LessEqual: return i <= i2;
                            default: return false;
                        }
                    }

                default: return false;
            }
        }
        /// <summary>
        /// This routine assigns Structure definitions to 
        /// </summary>
        private void MatchRecordStructureToRecord()
        {
            //SubrecordStructs = null;
            if (RecordStructure.Records == null) return;
            if (!RecordStructure.Records.ContainsKey(parentRecord.Name)) return;
            //SubrecordStructs = new SubrecordStructure[parentRecord.SubRecords.Count];
            SubrecordStructure[] sss = RecordStructure.Records[parentRecord.Name].subrecords;
            SubRecord[] subs = parentRecord.SubRecords.ToArray();
            foreach (var sub in subs) sub.DetachStructure();
            int subi = 0, ssi = 0;
            Stack<LoopBlock> repeats = new Stack<LoopBlock>();
            Dictionary<int, Conditional> conditions = new Dictionary<int, Conditional>();
            while (subi < subs.Length && ssi < sss.Length)
            {
                if (sss[ssi].Condition != CondType.None && !MatchRecordCheckCondition(conditions, sss[ssi]))
                {
                    ssi++;
                    continue;
                }
                if (subs[subi].Name == sss[ssi].name && (sss[ssi].size == 0 || sss[ssi].size == subs[subi].Size))
                {
                    subs[subi].AttachStructure(sss[ssi]);
                    if (sss[ssi].repeat > 0)
                    {
                        if (repeats.Count == 0 || repeats.Peek().start != ssi) repeats.Push(new LoopBlock(ssi, ssi + sss[ssi].repeat));
                    }
                    if (sss[ssi].ContaintsConditionals)
                    {
                        try
                        {
                            MatchRecordAddConditionals(conditions, subs[subi], sss[ssi].elements);
                        }
                        catch { }
                    }
                    subi++;
                    ssi++;
                }
                else if (repeats.Count > 0 && repeats.Peek().start == ssi)
                {
                    ssi = repeats.Pop().end;
                }
                else if (sss[ssi].optional > 0)
                {
                    ssi += sss[ssi].optional;
                }
                else return;
                if (repeats.Count > 0 && repeats.Peek().end == ssi) ssi = repeats.Peek().start;
            }
        }

        private void PluginTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            RebuildSelection();
        }

        void RebuildSelection()
        {
            if (PluginTree.SelectedNode == null)
                return;
            SelectedSubrecord = false;
            parentRecord = null;
            FindMasters();
            if (PluginTree.SelectedNode.Tag is Plugin)
            {
                listSubrecord.DataSource = null;
                tbInfo.Text = ((BaseRecord)PluginTree.SelectedNode.Tag).GetDesc();
                cutToolStripMenuItem.Enabled = false;
                copyToolStripMenuItem.Enabled = false;
                deleteToolStripMenuItem.Enabled = false;
                pasteToolStripMenuItem.Enabled = true;
                insertRecordToolStripMenuItem.Enabled = true;
                insertSubrecordToolStripMenuItem.Enabled = false;
            }
            else if (PluginTree.SelectedNode.Tag is Record)
            {
                cutToolStripMenuItem.Enabled = true;
                copyToolStripMenuItem.Enabled = true;
                deleteToolStripMenuItem.Enabled = true;
                pasteToolStripMenuItem.Enabled = true;
                insertRecordToolStripMenuItem.Enabled = false;
                insertSubrecordToolStripMenuItem.Enabled = true;
                Record r = (Record)PluginTree.SelectedNode.Tag;
                listSubrecord.DataSource = r.SubRecords;
                parentRecord = r;
                MatchRecordStructureToRecord();
                tbInfo.Text = ((Record)PluginTree.SelectedNode.Tag).GetDesc(
                    lookupFormidsToolStripMenuItem.Checked ? new dFormIDLookupI(LookupFormIDI) : null
                    , lookupFormidsToolStripMenuItem.Checked ? new dLStringLookup(LookupFormStrings) : null
                    );
            }
            else
            {
                listSubrecord.DataSource = null;
                tbInfo.Text = ((BaseRecord)PluginTree.SelectedNode.Tag).GetDesc();
                cutToolStripMenuItem.Enabled = true;
                copyToolStripMenuItem.Enabled = true;
                deleteToolStripMenuItem.Enabled = true;
                pasteToolStripMenuItem.Enabled = true;
                insertRecordToolStripMenuItem.Enabled = true;
                insertSubrecordToolStripMenuItem.Enabled = false;
            }
        }

        private void RefreshSelection()
        {
            if (PluginTree.SelectedNode == null) 
                return;

            //Enable and disable relevant menu items
            if (PluginTree.SelectedNode.Tag is Plugin)
            {
                tbInfo.Text = ((BaseRecord)PluginTree.SelectedNode.Tag).GetDesc();
                cutToolStripMenuItem.Enabled = false;
                copyToolStripMenuItem.Enabled = false;
                deleteToolStripMenuItem.Enabled = false;
                pasteToolStripMenuItem.Enabled = true;
                insertRecordToolStripMenuItem.Enabled = true;
                insertSubrecordToolStripMenuItem.Enabled = false;
            }
            else if (PluginTree.SelectedNode.Tag is Record)
            {
                Record r = (Record)PluginTree.SelectedNode.Tag;
                if (!r.Equals(parentRecord))
                {
                    RebuildSelection();
                }
                else
                {
                    cutToolStripMenuItem.Enabled = true;
                    copyToolStripMenuItem.Enabled = true;
                    deleteToolStripMenuItem.Enabled = true;
                    pasteToolStripMenuItem.Enabled = true;
                    insertRecordToolStripMenuItem.Enabled = false;
                    insertSubrecordToolStripMenuItem.Enabled = true;
                    tbInfo.Text = ((Record)PluginTree.SelectedNode.Tag).GetDesc(
                        lookupFormidsToolStripMenuItem.Checked ? new dFormIDLookupI(LookupFormIDI) : null
                        , lookupFormidsToolStripMenuItem.Checked ? new dLStringLookup(LookupFormStrings) : null
                        );
                }
            }
            else
            {
                tbInfo.Text = ((BaseRecord)PluginTree.SelectedNode.Tag).GetDesc();
                cutToolStripMenuItem.Enabled = true;
                copyToolStripMenuItem.Enabled = true;
                deleteToolStripMenuItem.Enabled = true;
                pasteToolStripMenuItem.Enabled = true;
                insertRecordToolStripMenuItem.Enabled = true;
                insertSubrecordToolStripMenuItem.Enabled = false;
            }
            listSubrecord.Refresh();
        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ValidateMakeChange())
                return;
            if (SelectedSubrecord)
            {
                if (listSubrecord.SelectedIndices.Count != 1) return;
                parentRecord.SubRecords.RemoveAt(listSubrecord.SelectedIndices[0]);
                listSubrecord.Refresh();

            }
            else
            {
                if (PluginTree.SelectedNode.Parent != null)
                {
                    BaseRecord parent = (BaseRecord)PluginTree.SelectedNode.Parent.Tag;
                    BaseRecord node = (BaseRecord)PluginTree.SelectedNode.Tag;
                    parent.DeleteRecord(node);
                }
                PluginTree.SelectedNode.Remove();
            }
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (PluginTree.SelectedNode == null)
            {
                MessageBox.Show("No plugin selected to save", "Error");
                return;
            }
            TreeNode tn = PluginTree.SelectedNode;
            while (!(tn.Tag is Plugin)) tn = tn.Parent;
            Plugin p = (Plugin)tn.Tag;
            if (SaveModDialog.ShowDialog() == DialogResult.OK)
            {
                p.Save(SaveModDialog.FileName);
            }
            if (p.Name != tn.Text) tn.Text = p.Name;
        }

        private void cutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ValidateMakeChange())
                return;
            if (!SelectedSubrecord && PluginTree.SelectedNode != null && PluginTree.SelectedNode.Tag is Plugin)
            {
                MessageBox.Show("Cannot cut a plugin", "Error");
                return;
            }
            copyToolStripMenuItem_Click(null, null);
            deleteToolStripMenuItem_Click(null, null);
        }

        private void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedSubrecord)
            {
                if (listSubrecord.SelectedIndices.Count != 1) return;
                int idx = listSubrecord.SelectedIndices[0];

                SubRecord sr = (SubRecord)listSubrecord.DataSource[idx];
                Clipboard = (SubRecord)sr.Clone();
                ClipboardNode = null;
            }
            else if (PluginTree.SelectedNode.Tag is Plugin)
            {
                MessageBox.Show("Cannot copy a plugin", "Error");
            }
            else
            {
                BaseRecord node = ((BaseRecord)PluginTree.SelectedNode.Tag).Clone();
                Clipboard = node;
                ClipboardNode = (TreeNode)PluginTree.SelectedNode.Clone();
                ClipboardNode.Tag = node;
                if (ClipboardNode.Nodes.Count > 0)
                {
                    ClipboardNode.Nodes.Clear();
                    foreach (Rec r in ((GroupRecord)node).Records) WalkPluginTree(r, ClipboardNode);
                }

            }
        }

        private void pasteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ValidateMakeChange())
                return;
            if (Clipboard == null)
            {
                MessageBox.Show("The clipboard is empty", "Error");
                return;
            }
            BaseRecord node = (BaseRecord)PluginTree.SelectedNode.Tag;
            if (Clipboard is Plugin)
            {
                MessageBox.Show("Plugin merging has been disabled");
                return;
            }
            try
            {
                node.AddRecord(Clipboard);
                Clipboard = Clipboard.Clone();
                if (ClipboardNode != null)
                {
                    PluginTree.SelectedNode.Nodes.Add(ClipboardNode);
                    ClipboardNode = (TreeNode)ClipboardNode.Clone();
                    ClipboardNode.Tag = Clipboard;
                }
                else
                {
                    PluginTree_AfterSelect(null, null);
                }
            }
            catch (TESParserException ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void newToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ValidateMakeChange())
                return;
            Plugin p = new Plugin();
            Record r = new Record();
            r.Name = "TES4";
            SubRecord sr = new SubRecord();
            sr.Name = "HEDR";
            sr.SetData(new byte[] { 0xD7, 0xA3, 0x70, 0x3F, 0xFA, 0x56, 0x0C, 0x00, 0x19, 0xEA, 0x07, 0xFF });
            r.AddRecord(sr);
            sr = new SubRecord();
            sr.Name = "CNAM";
            sr.SetData(System.Text.Encoding.ASCII.GetBytes("Default\0"));
            r.AddRecord(sr);
            p.AddRecord(r);
            TreeNode tn = new TreeNode(p.Name);
            tn.Tag = p;
            TreeNode tn2 = new TreeNode(r.DescriptiveName);
            tn2.Tag = r;
            tn.Nodes.Add(tn2);
            PluginTree.Nodes.Add(tn);
            UpdateStringEditor();
        }

        private void PluginTree_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (PluginTree.SelectedNode == null) return;
            if (PluginTree.SelectedNode.Tag is Record)
            {
                Record r = (Record)PluginTree.SelectedNode.Tag;
                HeaderEditor.Display(r);
                if (PluginTree.SelectedNode.Text != r.DescriptiveName)
                    PluginTree.SelectedNode.Text = r.DescriptiveName;
                tbInfo.Text = ((BaseRecord)PluginTree.SelectedNode.Tag).GetDesc();
            }
            else if (PluginTree.SelectedNode.Tag is GroupRecord)
            {
                GroupRecord gr = (GroupRecord)PluginTree.SelectedNode.Tag;
                GroupEditor.Display(gr);
                if (PluginTree.SelectedNode.Text != gr.DescriptiveName)
                    PluginTree.SelectedNode.Text = gr.DescriptiveName;
                tbInfo.Text = ((BaseRecord)PluginTree.SelectedNode.Tag).GetDesc();
            }
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            var sr = GetSelectedSubrecord();
            if (sr == null) return;

            if (sr.Structure != null && sr.Structure.elements != null)
            {
                tbInfo.Text = sr.GetFormattedData(LookupFormIDI, LookupFormStrings);
            }
            else
            {
                tbInfo.Text = "[Subrecord data]" + Environment.NewLine 
                    + "String: " + sr.GetStrData() + Environment.NewLine + Environment.NewLine 
                    + "Hex:" + Environment.NewLine + sr.GetHexData();
            }
            pasteToolStripMenuItem.Enabled = false;
            copyToolStripMenuItem.Enabled = true;
            cutToolStripMenuItem.Enabled = true;
            deleteToolStripMenuItem.Enabled = true;
            SelectedSubrecord = true;
            insertRecordToolStripMenuItem.Enabled = false;
            insertSubrecordToolStripMenuItem.Enabled = false;
        }

        private void listView1_ItemActivate(object sender, EventArgs e)
        {
            //EditSelectedSubrecord();
        }

        void EditSelectedSubrecord()
        {
            var sr = GetSelectedSubrecord();
            if (sr == null)return;

            if (useNewSubrecordEditorToolStripMenuItem.Checked 
                && sr.Structure != null
                && sr.Structure.elements != null 
                && sr.Structure.elements[0].type != ElementValueType.Blob && !sr.Structure.UseHexEditor)
            {
                MediumLevelRecordEditor re;
                try
                {
                    re = new MediumLevelRecordEditor(sr, sr.Structure, LookupFormIDS, FormIDScan, LookupFormStrings);
                }
                catch
                {
                    MessageBox.Show("Subrecord doesn't seem to conform to the expected structure.", "Error");
                    re = null;
                }
                if (re != null)
                {
                    re.ShowDialog();
                    tbInfo.Text = sr.GetFormattedData(LookupFormIDI, LookupFormStrings);
                    if (sr.Name == "EDID" && listSubrecord.SelectedIndices[0] == 0)
                    {
                        parentRecord.descriptiveName = " (" + sr.GetStrData() + ")";
                        PluginTree.SelectedNode.Text = parentRecord.DescriptiveName;
                    }
                    //listSubrecord.SelectedItems[0].SubItems[1].Text = sr.Size.ToString() + " *";
                    listSubrecord.Refresh();
                    return;
                }
            }
            if (hexModeToolStripMenuItem.Checked)
            {
                new HexDataEdit(sr.Name, sr.GetData(), LookupFormIDS).ShowDialog();
                if (!HexDataEdit.Canceled)
                {
                    sr.SetData(HexDataEdit.result);
                    sr.Name = HexDataEdit.resultName;
                    //listSubrecord.SelectedItems[0].Text = sr.Name;
                    //listSubrecord.SelectedItems[0].SubItems[1].Text = sr.Size.ToString();
                    tbInfo.Text = "[Subrecord data]" + Environment.NewLine + sr.GetHexData();
                    listSubrecord.Refresh();
                }
            }
            else
            {
                new DataEdit(sr.Name, sr.GetData()).ShowDialog();
                if (!DataEdit.Canceled)
                {
                    sr.SetData(DataEdit.result);
                    sr.Name = DataEdit.resultName;
                    //listSubrecord.SelectedItems[0].Text = sr.Name;
                    //listSubrecord.SelectedItems[0].SubItems[1].Text = sr.Size.ToString();
                    tbInfo.Text = "[Subrecord data]" + Environment.NewLine + sr.GetStrData();
                    listSubrecord.Refresh();
                }
            }
            MatchRecordStructureToRecord();
            if (sr.Name == "EDID" && listSubrecord.SelectedIndices[0] == 0)
            {
                parentRecord.descriptiveName = " (" + sr.GetStrData() + ")";
                PluginTree.SelectedNode.Text = parentRecord.DescriptiveName;
            }
        }
        void EditSelectedSubrecordHex()
        {
            var sr = GetSelectedSubrecord();
            if (sr == null)
                return;
            using (var form = new HexDataEdit(sr.Name, sr.GetData(), LookupFormIDS))
            {
                DialogResult result = form.ShowDialog();
                if (result == DialogResult.OK)
                {
                    sr.SetData(HexDataEdit.result);
                    sr.Name = HexDataEdit.resultName;
                    tbInfo.Text = "[Subrecord data]" + Environment.NewLine + sr.GetHexData();
                    listSubrecord.Refresh();
                }
            }
            MatchRecordStructureToRecord();
            if (sr.Name == "EDID" && listSubrecord.SelectedIndices[0] == 0)
            {
                parentRecord.descriptiveName = " (" + sr.GetStrData() + ")";
                PluginTree.SelectedNode.Text = parentRecord.DescriptiveName;
            }
        }

        private SubRecord GetSelectedSubrecord()
        {
            if (listSubrecord.SelectedIndices.Count != 1) return null;
            int idx = listSubrecord.SelectedIndices[0];
            return listSubrecord.DataSource[idx] as SubRecord;
        }

        private void insertRecordToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ValidateMakeChange())
                return;


            BaseRecord node = (BaseRecord)PluginTree.SelectedNode.Tag;
            Record p = new Record();
            node.AddRecord(p);
            TreeNode tn = new TreeNode(p.Name);
            tn.Tag = p;
            PluginTree.SelectedNode.Nodes.Add(tn);
        }

        private void insertSubrecordToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ValidateMakeChange())
                return;
            BaseRecord node = (BaseRecord)PluginTree.SelectedNode.Tag;
            SubRecord p = new SubRecord();
            node.AddRecord(p);
            PluginTree_AfterSelect(null, null);
        }

        private void findToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (searchForm != null)
            {
                searchForm.Focus();
            }
            else
            {
                spellsToolStripMenuItem.Enabled = false;
                searchForm = new SearchForm(PluginTree);
                searchForm.FormClosed += new FormClosedEventHandler(searchForm_FormClosed);
                searchForm.Show();
            }
        }

        private void searchForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            searchForm = null;
            spellsToolStripMenuItem.Enabled = true;
        }

        private void TESsnip_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (searchForm != null) searchForm.Close();
            PluginTree.Nodes.Clear();
            Clipboard = null;
            ClipboardNode = null;
            parentRecord = null;
            CloseStringEditor();
            Settings.SetWindowPosition("TESsnip", this);
        }

        private void tbInfo_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.Control)
            {
                if (e.KeyCode == Keys.A)
                {
                    tbInfo.SelectAll();
                }
                else if (e.KeyCode == Keys.C)
                {
                    tbInfo.Copy();
                }
            }
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (PluginTree.SelectedNode == null)
            {
                MessageBox.Show("No plugin selected to save", "Error");
                return;
            }
            if (!ValidateMakeChange())
                return;
            TreeNode tn = PluginTree.SelectedNode;
            while (!(tn.Tag is Plugin)) tn = tn.Parent;
            tn.Tag = null;
            PluginTree.Nodes.Remove(tn);
            UpdateStringEditor();
        }

        private bool DragDropInProgress;
        private void listView1_ItemDrag(object sender, ItemDragEventArgs e)
        {
            if (listSubrecord.SelectedIndices.Count != 1 || e.Button != MouseButtons.Left) return;
            DragDropInProgress = true;
            listSubrecord.DoDragDrop(listSubrecord.SelectedIndices[0] + 1, DragDropEffects.Move);
        }

        private void listView1_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            System.Drawing.Point p = listSubrecord.PointToClient(Form.MousePosition);
            ListViewItem lvi = listSubrecord.GetItemAt(p.X, p.Y);
            if (lvi == null) listSubrecord.SelectedIndices.Clear();
            else lvi.Selected = true;
        }

        private void listView1_DragDrop(object sender, DragEventArgs e)
        {
            int toswap = (int)e.Data.GetData(typeof(int)) - 1;
            if (toswap == -1) return;
            SubRecord sr = parentRecord.SubRecords[toswap];
            if (listSubrecord.SelectedIndices.Count == 0)
            {
                parentRecord.SubRecords.RemoveAt(toswap);
                parentRecord.SubRecords.Add(sr);
            }
            else if (listSubrecord.SelectedIndices.Count == 1)
            {
                int moveto = listSubrecord.SelectedIndices[0];
                if (toswap == moveto) return;
                parentRecord.SubRecords.RemoveAt(toswap);
                parentRecord.SubRecords.Insert(moveto, sr);
            }
            else return;
            PluginTree_AfterSelect(null, null);
            return;
        }

        private void listView1_DragEnter(object sender, DragEventArgs e)
        {
            if (!DragDropInProgress) return;
            e.Effect = DragDropEffects.Move;
            DragDropInProgress = false;
        }

        #region Spells
        private readonly string[] SanitizeOrder = new string[] {
            "GMST", "TXST", "MICN", "GLOB", "CLAS", "FACT", "HDPT", "HAIR", "EYES", "RACE", "SOUN", "ASPC", "MGEF", "SCPT", "LTEX",
            "ENCH", "SPEL", "ACTI", "TACT", "TERM", "ARMO", "BOOK", "CONT", "DOOR", "INGR", "LIGH", "MISC", "STAT", "SCOL", "MSTT",
            "PWAT", "GRAS", "TREE", "FURN", "WEAP", "AMMO", "NPC_", "CREA", "LVLC", "LVLN", "KEYM", "ALCH", "IDLM", "NOTE", "COBJ",
            "PROJ", "LVLI", "WTHR", "CLMT", "REGN", "NAVI", "CELL", "WRLD", "DIAL", "QUST", "IDLE", "PACK", "CSTY", "LSCR", "ANIO",
            "WATR", "EFSH", "EXPL", "DEBR", "IMGS", "IMAD", "FLST", "PERK", "BPTD", "ADDN", "AVIF", "RADS", "CAMS", "CPTH", "VTYP",
            "IPCT", "IPDS", "ARMA", "ECZN", "MESG", "RGDL", "DOBJ", "LGTM", "MUSC"
        };
        private int sanitizeCountRecords(Rec r)
        {
            if (r is Record) return 1;
            else
            {
                int i = 1;
                foreach (Rec r2 in ((GroupRecord)r).Records) i += sanitizeCountRecords(r2);
                return i;
            }
        }
        private void sanitizeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (PluginTree.SelectedNode == null)
            {
                MessageBox.Show("No plugin selected", "Error");
                return;
            }
            TreeNode tn = PluginTree.SelectedNode;
            while (!(tn.Tag is Plugin)) tn = tn.Parent;
            Plugin p = (Plugin)tn.Tag;

            Queue<Rec> toParse = new Queue<Rec>(p.Records);
            if (toParse.Count == 0 || toParse.Peek().Name != "TES4")
            {
                MessageBox.Show("Plugin lacks a vlid TES4 record. Cannot continue");
                return;
            }

            tn.Nodes.Clear();
            p.Records.Clear();
            p.AddRecord(toParse.Dequeue());
            Dictionary<string, GroupRecord> groups = new Dictionary<string, GroupRecord>();

            GroupRecord gr;
            Record r2;

            foreach (string s in SanitizeOrder)
            {
                gr = new GroupRecord(s);
                p.Records.Add(gr);
                groups[s] = gr;
            }

            bool looseGroupsWarning = false;
            bool unknownRecordsWarning = false;
            while (toParse.Count > 0)
            {
                Rec r = toParse.Dequeue();
                if (r is GroupRecord)
                {
                    gr = (GroupRecord)r;
                    if (gr.ContentsType == "CELL" || gr.ContentsType == "WRLD" || gr.ContentsType == "DIAL")
                    {
                        groups[gr.ContentsType].Records.AddRange(gr.Records);
                        gr.Records.Clear();
                    }
                    else
                    {
                        for (int i = 0; i < gr.Records.Count; i++) toParse.Enqueue(gr.Records[i]);
                        gr.Records.Clear();
                    }
                }
                else
                {
                    r2 = (Record)r;
                    if (r2.Name == "CELL" || r2.Name == "WRLD" || r2.Name == "REFR" || r2.Name == "ACRE" || r2.Name == "ACHR" || r2.Name == "NAVM" || r2.Name == "DIAL" || r2.Name == "INFO")
                    {
                        looseGroupsWarning = true;
                        p.AddRecord(r2);
                    }
                    else
                    {
                        if (groups.ContainsKey(r2.Name)) groups[r2.Name].AddRecord(r2);
                        else
                        {
                            unknownRecordsWarning = true;
                            p.AddRecord(r2);
                        }
                    }
                }
            }

            foreach (GroupRecord gr2 in groups.Values)
            {
                if (gr2.Records.Count == 0) p.DeleteRecord(gr2);
            }



            if (looseGroupsWarning)
            {
                MessageBox.Show("The subgroup structure of this plugins cell, world or dial records appears to be incorrect, and cannot be fixed automatically",
                    "Warning");
            }
            if (unknownRecordsWarning)
            {
                MessageBox.Show("The plugin contained records which were not recognised, and cannot be fixed automatically",
                    "Warning");
            }



            CreatePluginTree(p, tn);

            int reccount = -1;
            foreach (Rec r in p.Records) reccount += sanitizeCountRecords(r);
            if (p.Records.Count > 0 && p.Records[0].Name == "TES4")
            {
                Record tes4 = (Record)p.Records[0];
                if (tes4.SubRecords.Count > 0 && tes4.SubRecords[0].Name == "HEDR" && tes4.SubRecords[0].Size >= 8)
                {
                    byte[] data = tes4.SubRecords[0].GetData();
                    byte[] reccountbytes = TypeConverter.si2h(reccount);
                    for (int i = 0; i < 4; i++) data[4 + i] = reccountbytes[i];
                    tes4.SubRecords[0].SetData(data);
                }
            }
        }

        private void StripEDIDspublic(Rec r)
        {
            if (r is Record)
            {
                Record r2 = (Record)r;
                if (r2.Name != "GMST" && r2.SubRecords.Count > 0 && r2.SubRecords[0].Name == "EDID") r2.DeleteRecord(r2.SubRecords[0]);
                for (int i = 0; i < r2.SubRecords.Count; i++)
                {
                    if (r2.SubRecords[i].Name == "SCTX") r2.SubRecords.RemoveAt(i--);
                }
            }
            else
            {
                foreach (Rec r2 in ((GroupRecord)r).Records) StripEDIDspublic(r2);
            }
        }
        private void stripEDIDsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (PluginTree.SelectedNode == null)
            {
                MessageBox.Show("No plugin selected", "Error");
                return;
            }
            if (MessageBox.Show("If you don't know what this does, you probably don't want to click it\nContinue anyway?",
                "Warning", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            TreeNode tn = PluginTree.SelectedNode;
            while (!(tn.Tag is Plugin)) tn = tn.Parent;
            Plugin p = (Plugin)tn.Tag;
            foreach (Rec r in p.Records) StripEDIDspublic(r);
        }

        private bool findDuplicateFormIDs(TreeNode tn, Dictionary<uint, TreeNode> ids)
        {
            if (tn.Tag is Record)
            {
                Record r2 = (Record)tn.Tag;
                if (ids.ContainsKey(r2.FormID))
                {
                    PluginTree.SelectedNode = tn;
                    MessageBox.Show("Record duplicates " + ((Record)ids[r2.FormID].Tag).DescriptiveName);
                    ids.Clear();
                    return true;
                }
                else
                {
                    ids.Add(r2.FormID, tn);
                }
            }
            else
            {
                foreach (TreeNode tn2 in tn.Nodes) findDuplicateFormIDs(tn2, ids);
            }
            return false;
        }
        private void findDuplicatedFormIDToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode tn = PluginTree.SelectedNode;
            while (tn.Parent != null) tn = tn.Parent;
            Dictionary<uint, TreeNode> ids = new Dictionary<uint, TreeNode>();
            foreach (TreeNode tn2 in tn.Nodes)
            {
                if (findDuplicateFormIDs(tn2, ids)) return;
            }
            ids.Clear();
        }

        private void DumpEdidsInternal(Rec r, System.IO.StreamWriter sw)
        {
            if (r is Record)
            {
                Record r2 = (Record)r;
                if (r2.SubRecords.Count > 0 && r2.SubRecords[0].Name == "EDID") sw.WriteLine(r2.SubRecords[0].GetStrData());
            }
            else
            {
                foreach (Rec r2 in ((GroupRecord)r).Records) DumpEdidsInternal(r2, sw);
            }
        }
        private void dumpEDIDListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (PluginTree.SelectedNode == null) return;
            if (PluginTree.SelectedNode.Tag is Record)
            {
                MessageBox.Show("Spell works only on plugins or record groups", "Error");
                return;
            }
            if (SaveEdidListDialog.ShowDialog() != DialogResult.OK) return;
            System.IO.StreamWriter sw = new System.IO.StreamWriter(SaveEdidListDialog.FileName);
            if (PluginTree.SelectedNode.Tag is Plugin)
            {
                foreach (Rec r in ((Plugin)PluginTree.SelectedNode.Tag).Records)
                {
                    DumpEdidsInternal(r, sw);
                }
            }
            else
            {
                DumpEdidsInternal((GroupRecord)PluginTree.SelectedNode.Tag, sw);
            }
            sw.Close();
        }

        private void cleanRecurse(Rec r, uint match, uint mask, Dictionary<uint, Record> lookup)
        {
            Record r2 = r as Record;
            if (r2 != null)
            {
                if ((r2.FormID & 0xff000000) == match)
                {
                    if (r2.Name != "REFR" && r2.Name != "ACHR" && r2.Name != "NAVM" && r2.Name != "INFO") lookup[(r2.FormID & 0xffffff) | mask] = r2;
                }
            }
            else
            {
                foreach (Rec r3 in ((GroupRecord)r).Records)
                {
                    cleanRecurse(r3, match, mask, lookup);
                }
            }
        }
        private bool cleanRecurse2(Rec r, ref int count, Dictionary<uint, Record> lookup)
        {
            Record r2 = r as Record;
            if (r2 != null)
            {
                if (lookup.ContainsKey(r2.FormID))
                {
                    Record r3 = lookup[r2.FormID];
                    if (r2.Name == r3.Name && r2.Size == r3.Size && r2.SubRecords.Count == r3.SubRecords.Count && r2.Flags1 == r3.Flags1 &&
                        r2.Flags2 == r3.Flags2 && r2.Flags3 == r3.Flags3)
                    {
                        for (int i = 0; i < r2.SubRecords.Count; i++)
                        {
                            if (r2.SubRecords[i].Name != r3.SubRecords[i].Name || r2.SubRecords[i].Size != r3.SubRecords[i].Size) return false;
                            byte[] data1 = r2.SubRecords[i].GetReadonlyData(), data2 = r3.SubRecords[i].GetReadonlyData();
                            for (int j = 0; j < data1.Length; j++) if (data1[j] != data2[j]) return false;
                        }
                        return true;
                    }
                }
            }
            else
            {
                GroupRecord gr = (GroupRecord)r;
                for (int i = 0; i < gr.Records.Count; i++)
                {
                    if (cleanRecurse2(gr.Records[i], ref count, lookup))
                    {
                        count++;
                        gr.Records.RemoveAt(i--);
                    }
                }
            }
            return false;
        }
        private void cleanEspToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (PluginTree.SelectedNode == null)
            {
                MessageBox.Show("No plugin selected", "Error");
                return;
            }
            if (MessageBox.Show("This may delete records from the esp.\nAre you sure you wish to continue?", "Warning", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            FindMasters();
            Dictionary<uint, Record> lookup = new Dictionary<uint, Record>();
            bool missingMasters = false;
            for (int i = 0; i < FormIDLookup.Length - 1; i++)
            {
                if (FormIDLookup[i] == null)
                {
                    missingMasters = true;
                    continue;
                }
                if (FormIDLookup[i].Records.Count < 2 || FormIDLookup[i].Records[0].Name != "TES4") continue;
                uint match = 0;
                foreach (SubRecord sr in ((Record)FormIDLookup[i].Records[0]).SubRecords) if (sr.Name == "MAST") match++;
                match <<= 24;
                uint mask = (uint)i << 24;
                for (int j = 1; j < FormIDLookup[i].Records.Count; j++) cleanRecurse(FormIDLookup[i].Records[j], match, mask, lookup);
            }

            if (missingMasters)
            {
                MessageBox.Show("One or more dependencies are not loaded, and will be ignored.", "Warning");
            }

            int count = 0;
            for (int j = 1; j < FormIDLookup[FormIDLookup.Length - 1].Records.Count; j++)
                cleanRecurse2(FormIDLookup[FormIDLookup.Length - 1].Records[j], ref count, lookup);
            if (count == 0) MessageBox.Show("No records removed");
            else MessageBox.Show("" + count + " records removed");

            TreeNode tn = PluginTree.SelectedNode;
            while (tn.Parent != null) tn = tn.Parent;
            tn.Nodes.Clear();
            CreatePluginTree(FormIDLookup[FormIDLookup.Length - 1], tn);
        }

        private bool findNonConformingRecordInternal(TreeNode tn)
        {
            if (tn.Tag is Record)
            {
                Record r = tn.Tag as Record;
                PluginTree.SelectedNode = tn;
                foreach (var sr in r.SubRecords)
                {
                    if (sr.Structure == null)
                        return true;
                }
            }
            else
            {
                foreach (TreeNode tn2 in tn.Nodes) if (findNonConformingRecordInternal(tn2)) return true;
            }
            return false;
        }
        private void findNonconformingRecordToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (PluginTree.SelectedNode == null) return;
            if (PluginTree.SelectedNode.Tag is Record)
            {
                MessageBox.Show("Spell works only on plugins or record groups", "Error");
                return;
            }
            findNonConformingRecordInternal(PluginTree.SelectedNode);
        }

        private void compileScriptToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (FormIDLookup == null || PluginTree.SelectedNode == null) return;
            string errors;
            Record r;
            if (SelectedSubrecord && parentRecord.Name != "SCPT")
            {
                var sr = GetSelectedSubrecord();
                if (sr == null) return;
                if (sr.Name != "SCTX")
                {
                    MessageBox.Show("You need to select a SCPT record or SCTX subrecord to compile", "Error");
                    return;
                }
                ScriptCompiler.ScriptCompiler.Setup(FormIDLookup);
                if (!ScriptCompiler.ScriptCompiler.CompileResultScript(sr, out r, out errors))
                {
                    MessageBox.Show("There were compilation errors:\n" + errors);
                }
                else
                {
                    int i = listSubrecord.SelectedIndices[0];
                    var srs = parentRecord.SubRecords;
                    while (i > 0 && (srs[i - 1].Name == "SCDA" || srs[i - 1].Name == "SCHR"))
                        srs.RemoveAt(--i);
                    while (i < srs.Count && (srs[i].Name == "SCTX" || srs[i].Name == "SLSD" || srs[i].Name == "SCVR" || srs[i].Name == "SCRO" || srs[i].Name == "SCRV"))
                        srs.RemoveAt(i);

                    srs.InsertRange(i, r.SubRecords);

                    PluginTree_AfterSelect(null, null);
                }
                return;
            }
            TreeNode tn = PluginTree.SelectedNode;
            r = tn.Tag as Record;
            if (r == null || (r.Name != "SCPT"))
            {
                MessageBox.Show("You need to select a SCPT record or SCTX subrecord to compile", "Error");
                return;
            }

            ScriptCompiler.ScriptCompiler.Setup(FormIDLookup);
            if (!ScriptCompiler.ScriptCompiler.Compile(r, out errors))
            {
                MessageBox.Show("There were compilation errors:\n" + errors);
            }
            else
            {
                PluginTree_AfterSelect(null, null);
            }
        }

        private void compileAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string errors;
            string thingy = "";
            int count = 0, failed = 0, failed2 = 0;
            int size;
            ScriptCompiler.ScriptCompiler.Setup(FormIDLookup);
            TreeNode tn = PluginTree.SelectedNode;
            while (tn.Parent != null) tn = tn.Parent;
            foreach (Rec rec in ((Plugin)tn.Tag).Records)
            {
                GroupRecord gr = rec as GroupRecord;
                if (gr == null) continue;
                if (gr.ContentsType == "SCPT")
                {
                    foreach (Record r in gr.Records)
                    {
                        count++;
                        size = 0;
                        foreach (SubRecord sr in r.SubRecords)
                        {
                            if (sr.Name == "SCDA")
                            {
                                size = (int)sr.Size;
                                break;
                            }
                        }
                        if (!ScriptCompiler.ScriptCompiler.Compile(r, out errors))
                        {
                            failed++;
                            thingy += r.DescriptiveName + Environment.NewLine + errors + Environment.NewLine + Environment.NewLine;
                        }
                        else
                        {
                            foreach (SubRecord sr in r.SubRecords)
                            {
                                if (sr.Name == "SCDA")
                                {
                                    if (sr.Size != size)
                                    {
                                        failed2++;
                                        thingy += r.DescriptiveName + Environment.NewLine + "Size changed from " + size + " to " + sr.Size + Environment.NewLine + Environment.NewLine;
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            thingy += Environment.NewLine + Environment.NewLine + "Final results: " + count + "/" + failed + "/" + failed2;
            System.IO.File.WriteAllText("script results.txt", thingy);
        }

        private void generateLLXmlToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (PluginTree.SelectedNode == null) return;
            Plugin p = FormIDLookup[FormIDLookup.Length - 1];

            {
                Record r;
                if (p.Records.Count > 0) r = p.Records[0] as Record;
                else r = null;
                bool firstwasfallout = false;
                if (r != null && r.Name == "TES4")
                {
                    foreach (SubRecord sr in r.SubRecords)
                    {
                        if (sr.Name == "MAST")
                        {
                            if (sr.GetStrData().ToLowerInvariant() == "skyrim.esm") firstwasfallout = true;
                            break;
                        }
                    }
                }
                if (!firstwasfallout)
                {
                    MessageBox.Show("Only works on plugin's whose first master is Skyrim.esm", "Error");
                    return;
                }
            }

            uint mask = (uint)(FormIDLookup.Length - 1) << 24;
            Queue<Rec> recs = new Queue<Rec>(p.Records);

            System.Text.StringBuilder sb2 = new System.Text.StringBuilder();
            System.Text.StringBuilder sb3 = new System.Text.StringBuilder();
            while (recs.Count > 0)
            {
                Rec rec = recs.Dequeue();
                if (rec is GroupRecord)
                {
                    GroupRecord gr = (GroupRecord)rec;
                    if (gr.ContentsType == "LVLI" || gr.ContentsType == "LVLN" || gr.ContentsType == "LVLC")
                    {
                        for (int i = 0; i < gr.Records.Count; i++) recs.Enqueue(gr.Records[i]);
                    }
                }
                else
                {
                    Record r = (Record)rec;
                    if ((r.FormID & 0xff000000) != 0) continue;
                    switch (r.Name)
                    {
                        case "LVLI":
                            for (int i = 0; i < r.SubRecords.Count; i++)
                            {
                                if (r.SubRecords[i].Name == "LVLO")
                                {
                                    if (r.SubRecords[i].Size != 12) continue;
                                    byte[] data = r.SubRecords[i].GetReadonlyData();
                                    uint formid = TypeConverter.h2i(data[4], data[5], data[6], data[7]);
                                    if ((formid & 0xff000000) != mask) continue;
                                    sb3.Append("      <Element level=\"" + TypeConverter.h2ss(data[0], data[1]) + "\" formid=\"" +
                                        (formid & 0xffffff).ToString("X6") + "\" count=\"" + TypeConverter.h2ss(data[8], data[9]) + "\" ");
                                    if (i < r.SubRecords.Count - 1 && r.SubRecords[i + 1].Name == "COED" && r.SubRecords[i + 1].Size == 12)
                                    {
                                        i++;
                                        data = r.SubRecords[i].GetReadonlyData();
                                        sb3.Append(" coed1=\"" + TypeConverter.h2i(data[0], data[1], data[2], data[3]) + "\" coed2=\"" +
                                            TypeConverter.h2i(data[4], data[5], data[6], data[7]) + "\" coed3=\"" +
                                            TypeConverter.h2i(data[8], data[9], data[10], data[11]) + "\" ");
                                    }
                                    sb3.AppendLine("/>");
                                }
                            }
                            if (sb3.Length > 0)
                            {
                                sb2.AppendLine("    <LVLI formid=\"" + r.FormID.ToString("X6") + "\">");
                                sb2.Append(sb3.ToString());
                                sb2.AppendLine("    </LVLI>");
                            }
                            sb3.Length = 0;
                            break;
                        case "LVLN":
                            for (int i = 0; i < r.SubRecords.Count; i++)
                            {
                                if (r.SubRecords[i].Name == "LVLO")
                                {
                                    if (r.SubRecords[i].Size != 12) continue;
                                    byte[] data = r.SubRecords[i].GetReadonlyData();
                                    uint formid = TypeConverter.h2i(data[4], data[5], data[6], data[7]);
                                    if ((formid & 0xff000000) != mask) continue;
                                    sb3.AppendLine("      <Element level=\"" + TypeConverter.h2ss(data[0], data[1]) + "\" formid=\"" +
                                        (formid & 0xffffff).ToString("X6") + "\" count=\"" + TypeConverter.h2ss(data[8], data[9]) + "\" />");
                                }
                            }
                            if (sb3.Length > 0)
                            {
                                sb2.AppendLine("    <LVLN formid=\"" + r.FormID.ToString("X6") + "\">");
                                sb2.Append(sb3.ToString());
                                sb2.AppendLine("    </LVLN>");
                            }
                            sb3.Length = 0;
                            break;
                        case "LVLC":
                            for (int i = 0; i < r.SubRecords.Count; i++)
                            {
                                if (r.SubRecords[i].Name == "LVLO")
                                {
                                    if (r.SubRecords[i].Size != 12) continue;
                                    byte[] data = r.SubRecords[i].GetReadonlyData();
                                    uint formid = TypeConverter.h2i(data[4], data[5], data[6], data[7]);
                                    if ((formid & 0xff000000) != mask) continue;
                                    sb3.AppendLine("      <Element level=\"" + TypeConverter.h2ss(data[0], data[1]) + "\" formid=\"" +
                                        (formid & 0xffffff).ToString("X6") + "\" count=\"" + TypeConverter.h2ss(data[8], data[9]) + "\" />");
                                }
                            }
                            if (sb3.Length > 0)
                            {
                                sb2.AppendLine("    <LVLC formid=\"" + r.FormID.ToString("X6") + "\">");
                                sb2.Append(sb3.ToString());
                                sb2.AppendLine("    </LVLC>");
                            }
                            sb3.Length = 0;
                            break;
                    }
                }
            }
            if (sb2.Length > 0)
            {
                System.Text.StringBuilder sb1 = new System.Text.StringBuilder();
                sb1.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
                sb1.AppendLine("<Plugin>");
                sb1.AppendLine("  <MergedLists>");
                sb1.Append(sb2);
                sb1.AppendLine("  </MergedLists>");
                sb1.AppendLine("</Plugin>");
                System.IO.File.WriteAllText(System.IO.Path.ChangeExtension("data\\" + p.Name, ".xml"), sb1.ToString());
            }
            else MessageBox.Show("No compatible levelled lists found");
        }
        /*
        private struct CellRecord {
            public int x;
            public int y;

            public CellRecord(int x, int y) {
                this.x=x;
                this.y=y;
            }
        }
        private void dumpWorldspaceHeightmapsToolStripMenuItem_Click(object sender, EventArgs e) {
            if(PluginTree.SelectedNode==null) return;
            GroupRecord gr=PluginTree.SelectedNode.Tag as GroupRecord;
            if(gr==null||gr.groupType!=1) {
                MessageBox.Show("You need to pick a world group to export");
                return;
            }
            GroupRecord gr2=(GroupRecord)PluginTree.SelectedNode.Parent.Tag;
            string wsname=((Record)gr2.Records[gr2.Records.IndexOf(gr)-1]).SubRecords[0].GetStrData();
            List<GroupRecord> lands=new List<GroupRecord>();
            List<Record> cells=new List<Record>();
            Queue<Rec> records=new Queue<Rec>(gr.Records);
            while(records.Count>0) {
                if(records.Peek() is Record) {
                    Record r=(Record)records.Dequeue();
                    if(r.Name=="CELL") cells.Add(r);
                } else {
                    gr2=(GroupRecord)records.Dequeue();
                    if(gr2.groupType==9&&gr2.Records.Count>0) {
                        foreach(Rec rr in gr2.Records) {
                            if(rr.Name=="LAND") {
                                lands.Add(gr2);
                                break;
                            }
                        }
                    } else if(gr2.groupType!=9) for(int i=0;i<gr2.Records.Count;i++) records.Enqueue(gr2.Records[i]);
                }
            }

            Dictionary<uint, CellRecord> cells2=new Dictionary<uint, CellRecord>();
            int minx=0, maxx=0, miny=0, maxy=0;
            foreach(Record r in cells) {
                foreach(SubRecord sr in r.SubRecords) {
                    if(sr.Name=="XCLC") {
                        byte[] data=sr.GetReadonlyData();
                        int x=TypeConverter.h2si(data[0], data[1], data[2], data[3]);
                        int y=TypeConverter.h2si(data[4], data[5], data[6], data[7]);
                        minx=Math.Min(x, minx);
                        maxx=Math.Max(x, maxx);
                        miny=Math.Min(y, miny);
                        maxy=Math.Max(y, maxy);
                        cells2[r.FormID]=new CellRecord(x, y);
                        break;
                    }
                }
            }

            System.Drawing.Bitmap bmp=new System.Drawing.Bitmap((1+maxx-minx)*32 + 1, (1+maxy-miny)*32 + 1);
            System.Drawing.Imaging.BitmapData bmpdata=bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            int[] pixels=new int[bmp.Width*bmp.Height];

            int minh=int.MaxValue;
            int maxh=int.MinValue;
            foreach(GroupRecord gr3 in lands) {
                byte[] data=gr3.GetData();
                CellRecord cell=cells2[TypeConverter.h2i(data[0], data[1], data[2], data[3])];

                Record land=null;
                foreach(Record r in gr3.Records) {
                    if(r.Name=="LAND") {
                        land=r;
                        break;
                    }
                }
                if(land==null) continue;
                foreach(SubRecord sr in land.SubRecords) {
                    if(sr.Name=="VHGT") {
                        data=sr.GetReadonlyData();
                        int offset=4;
                        int yheight=(int)TypeConverter.h2f(data[0], data[1], data[2], data[3]);
                        int height;
                        for(int y=0;y<33;y++) {
                            height=yheight;
                            yheight+=((sbyte)data[offset])*6;
                            for(int x=0;x<33;x++) {
                                height+=((sbyte)data[offset++])*6;
                                minh=Math.Min(height, minh);
                                maxh=Math.Max(height, maxh);
                                pixels[(cell.x-minx)*32+x + ((cell.y-miny)*32+y)*bmp.Width]=height;
                            }
                        }
                        break;
                    }
                }
            }

            //maxh=(maxh-minh)*255;
            //for(int i=0;i<pixels.Length;i++) {
            //    byte b=(byte)((pixels[i]-minh)/maxh);
            //    pixels[i]=b + (b<<8) + (b<<16) + (b<<24);
            //}

            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bmpdata.Scan0, pixels.Length);
            bmp.UnlockBits(bmpdata);
            bmp.Save("heightmap.bmp");
        }
        */
        private void makeEsmToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (PluginTree.SelectedNode == null) return;

            TreeNode tn = PluginTree.SelectedNode;
            while (!(tn.Tag is Plugin)) tn = tn.Parent;
            Plugin p = (Plugin)tn.Tag;

            if (p.Records.Count == 0 || p.Records[0].Name != "TES4")
            {
                MessageBox.Show("Plugin has no TES4 record");
                return;
            }

            Record tes4 = (Record)p.Records[0];
            if ((tes4.Flags1 & 1) == 1)
            {
                MessageBox.Show("Plugin is already a master file");
                return;
            }
            tes4.Flags1 |= 1;

            SaveModDialog.FileName = System.IO.Path.ChangeExtension(p.Name, ".esm");
            if (SaveModDialog.ShowDialog() == DialogResult.OK)
            {
                p.Save(SaveModDialog.FileName);
            }
            if (p.Name != tn.Text) tn.Text = p.Name;
        }

        private void martigensToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (PluginTree.SelectedNode == null) return;

            TreeNode tn = PluginTree.SelectedNode;
            while (!(tn.Tag is Plugin)) tn = tn.Parent;
            Plugin p = (Plugin)tn.Tag;


            Form f = new Form();
            f.Text = "Replace";
            TextBox tb = new TextBox();
            f.Controls.Add(tb);
            tb.Dock = DockStyle.Fill;
            tb.AcceptsReturn = true;
            tb.Multiline = true;
            tb.ScrollBars = ScrollBars.Vertical;
            f.ShowDialog();

            string replace = tb.Text;
            f.Text = "Replace with";
            tb.Text = "";
            f.ShowDialog();
            string with = tb.Text;

            Queue<Rec> recs = new Queue<Rec>(p.Records);
            while (recs.Count > 0)
            {
                if (recs.Peek() is GroupRecord)
                {
                    GroupRecord gr = (GroupRecord)recs.Dequeue();
                    for (int i = 0; i < gr.Records.Count; i++) recs.Enqueue(gr.Records[i]);
                }
                else
                {
                    Record r = (Record)recs.Dequeue();
                    foreach (SubRecord sr in r.SubRecords)
                    {
                        if (sr.Name != "SCTX") continue;
                        string text = sr.GetStrData();
                        int upto = 0;
                        bool replaced = false;
                        while (true)
                        {
                            int i = text.IndexOf(replace, upto, StringComparison.InvariantCultureIgnoreCase);
                            if (i == -1) break;
                            text = text.Remove(i, replace.Length).Insert(i, with);
                            upto = i + with.Length;
                            replaced = true;
                        }
                        if (replaced)
                        {
                            sr.SetStrData(text, false);
                        }
                    }
                }
            }
        }
        #endregion

        private void FindMasters()
        {
            TreeNode tn = PluginTree.SelectedNode;
            while (tn.Parent != null) tn = tn.Parent;
            Plugin p = (Plugin)tn.Tag;
            Plugin[] plugins = new Plugin[PluginTree.Nodes.Count];
            for (int i = 0; i < plugins.Length; i++) plugins[i] = (Plugin)PluginTree.Nodes[i].Tag;

            List<string> masters = new List<string>();
            if (p.Records.Count > 0 && p.Records[0].Name == "TES4")
            {
                foreach (SubRecord sr in ((Record)p.Records[0]).SubRecords)
                {
                    if (sr.Name == "MAST") masters.Add(sr.GetStrData().ToLowerInvariant());
                }
            }
            FormIDLookup = new Plugin[masters.Count + 1];
            Fixups = new uint[masters.Count + 1];
            for (int i = 0; i < masters.Count; i++)
            {
                for (int j = 0; j < plugins.Length; j++)
                {
                    if (masters[i] == plugins[j].Name.ToLowerInvariant())
                    {
                        FormIDLookup[i] = plugins[j];
                        uint fixup = 0;
                        if (plugins[j].Records.Count > 0 && plugins[j].Records[0].Name == "TES4")
                        {
                            foreach (SubRecord sr in ((Record)plugins[j].Records[0]).SubRecords)
                            {
                                if (sr.Name == "MAST") fixup++;
                            }
                        }
                        Fixups[i] = fixup;
                        break;
                    }
                }
            }
            FormIDLookup[masters.Count] = p;
            Fixups[masters.Count] = (uint)masters.Count;
        }

        private bool RecurseFormIDSearch(Rec rec, uint FormID, ref string edid)
        {
            if (rec is Record)
            {
                if (((Record)rec).FormID == FormID)
                {
                    edid = rec.DescriptiveName;
                    return true;
                }
            }
            else
            {
                foreach (Rec r in ((GroupRecord)rec).Records)
                {
                    if (RecurseFormIDSearch(r, FormID, ref edid)) return true;
                }
            }
            return false;
        }
        private string LookupFormIDI(uint id)
        {
            uint pluginid = (id & 0xff000000) >> 24;
            if (pluginid > FormIDLookup.Length)
            {
                return "FormID was invalid";
            }
            string edid = null;
            foreach (Rec r in FormIDLookup[FormIDLookup.Length - 1].Records)
            {
                if (RecurseFormIDSearch(r, id, ref edid)) return edid;
            }
            id &= 0xffffff;
            if (FormIDLookup[pluginid] == null)
            {
                return "Master not loaded";
            }
            id += Fixups[pluginid] << 24;
            foreach (Rec r in FormIDLookup[pluginid].Records)
            {
                if (RecurseFormIDSearch(r, id, ref edid)) return edid;
            }
            return "No match";
        }
        private string LookupFormIDS(string sid)
        {
            uint id;
            if (!uint.TryParse(sid, System.Globalization.NumberStyles.AllowHexSpecifier, null, out id))
            {
                return "FormID was invalid";
            }
            return LookupFormIDI(id);
        }

        private string LookupFormStrings(uint id)
        {
            string value = default(string);
            foreach (var plugin in FormIDLookup)
            {
                if (plugin == null) continue;

                if (plugin.Strings.TryGetValue(id, out value))
                    break;
                if (plugin.DLStrings.TryGetValue(id, out value))
                    break;
                if (plugin.ILStrings.TryGetValue(id, out value))
                    break;
            }
            return value;
        }


        private void FormIDScanRecurse(Rec r, uint match, uint mask, Dictionary<uint, string> table, string type)
        {
            Record r2 = r as Record;
            if (r2 != null)
            {
                if (r2.Name == type && (r2.FormID & 0xff000000) == match)
                {
                    table[(r2.FormID & 0xffffff) | mask] = r2.DescriptiveName;
                }
            }
            else
            {
                GroupRecord gr = (GroupRecord)r;
                if (gr.groupType == 0 && gr.ContentsType != type) return;
                foreach (Rec r3 in gr.Records)
                {
                    FormIDScanRecurse(r3, match, mask, table, type);
                }
            }
        }
        private void FormIDScanRecurse2(Rec r, Dictionary<uint, string> table, string type)
        {
            Record r2 = r as Record;
            if (r2 != null)
            {
                if (r2.Name == type)
                {
                    table[r2.FormID] = r2.DescriptiveName;
                }
            }
            else
            {
                GroupRecord gr = (GroupRecord)r;
                if (gr.groupType == 0 && gr.ContentsType != type) return;
                foreach (Rec r3 in gr.Records)
                {
                    FormIDScanRecurse2(r3, table, type);
                }
            }
        }
        private string[] FormIDScan(string type)
        {
            Dictionary<uint, string> list = new Dictionary<uint, string>();
            for (int i = 0; i < FormIDLookup.Length - 1; i++)
            {
                if (FormIDLookup[i] == null) continue;
                if (FormIDLookup[i].Records.Count < 2 || FormIDLookup[i].Records[0].Name != "TES4") continue;
                uint match = 0;
                foreach (SubRecord sr in ((Record)FormIDLookup[i].Records[0]).SubRecords) if (sr.Name == "MAST") match++;
                match <<= 24;
                uint mask = (uint)i << 24;
                for (int j = 1; j < FormIDLookup[i].Records.Count; j++) FormIDScanRecurse(FormIDLookup[i].Records[j], match, mask, list, type);
            }
            for (int j = 1; j < FormIDLookup[FormIDLookup.Length - 1].Records.Count; j++) FormIDScanRecurse2(FormIDLookup[FormIDLookup.Length - 1].Records[j], list, type);

            string[] ret = new string[list.Count * 2];
            int count = 0;
            foreach (KeyValuePair<uint, string> pair in list)
            {
                ret[count++] = pair.Value;
                ret[count++] = pair.Key.ToString("X8");
            }
            return ret;
        }

        private void reloadXmlToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                RecordStructure.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not parse RecordStructure.xml. Record-at-once editing will be unavailable.\n" + ex.Message, "Warning");
            }
        }

        private void editStringsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (stringEditor == null)
            {
                List<Plugin> plugins = new List<Plugin>();
                foreach (TreeNode node in PluginTree.Nodes)
                {
                    Plugin plugin = node.Tag as Plugin;
                    if (plugin == null)
                        continue;
                    plugins.Add(plugin);
                }

                if (plugins.Count == 0)
                {
                    MessageBox.Show(this, "No plugins available to edit", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                stringEditor = new Forms.StringsEditor();
                stringEditor.FormClosed += delegate(object a, FormClosedEventArgs args)
                {
                    CloseStringEditor();
                };
                stringEditor.Plugins = plugins.ToArray();
                stringEditor.Show(this); // modeless. Close if the tree is modified.
            }
        }
        private void UpdateStringEditor()
        {
            if (stringEditor != null)
            {
                List<Plugin> plugins = new List<Plugin>();
                foreach (TreeNode node in PluginTree.Nodes)
                {
                    Plugin plugin = node.Tag as Plugin;
                    if (plugin == null)
                        continue;
                    plugins.Add(plugin);
                }
                if (plugins.Count == 0)
                {
                    CloseStringEditor();
                }
                else
                {
                    stringEditor.Reload(plugins.ToArray());
                }
            }
        }
        private void CloseStringEditor()
        {
            if (this.stringEditor != null)
            {
                Forms.StringsEditor editor = this.stringEditor;
                this.stringEditor = null;
                try
                {
                    if (!editor.IsDisposed)
                        editor.Close();
                }
                catch { }

            }
        }

        private void MainView_Load(object sender, EventArgs e)
        {

        }

        private void PluginTree_Enter(object sender, EventArgs e)
        {
            PluginTree_AfterSelect(sender, null);
        }

        private void PluginTree_AfterExpand(object sender, TreeViewEventArgs e)
        {
            PluginTree_AfterSelect(sender, e);
        }

        #region SubRecord Manipulation

        private bool ValidateMakeChange()
        {
            if (searchForm != null && searchForm.InSearch)
            {
                MessageBox.Show("Cannot modify contents while searching", "Error");
                return false;
            }
            return true;
        }

        private void toolStripInsertRecord_Click(object sender, EventArgs e)
        {
            if (!ValidateMakeChange())
                return;
            try
            {
                BaseRecord br = (BaseRecord)PluginTree.SelectedNode.Tag;

                if (br is Record)
                {
                    if (listSubrecord.SelectedIndices.Count == 1) 
                    {
                        int idx = listSubrecord.SelectedIndices[0];
                        if (idx < 0 || idx >= (listSubrecord.Items.Count-1))
                            return;

                        Record r = (Record)br;
                        SubRecord p = new SubRecord();
                        r.InsertRecord(idx, p);
                    }
                    else
                    {
                        SubRecord p = new SubRecord();
                        br.AddRecord(p);
                    }

                }
                else
                {
                    SubRecord p = new SubRecord();
                    br.AddRecord(p);
                }
                PluginTree_AfterSelect(null, null);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void toolStripDeleteRecord_Click(object sender, EventArgs e)
        {
            if (!ValidateMakeChange())
                return;
            if (parentRecord != null)
            {
                if (listSubrecord.SelectedIndices.Count != 1) return;
                parentRecord.SubRecords.RemoveAt(listSubrecord.SelectedIndices[0]);
            }
            MatchRecordStructureToRecord();
            RefreshSelection();
        }

        private void toolStripMoveRecordUp_Click(object sender, EventArgs e)
        {
            if (listSubrecord.SelectedIndices.Count != 1) return;
            int idx = listSubrecord.SelectedIndices[0];
            if (idx < 1 || idx >= (listSubrecord.Items.Count))
                return;

            SubRecord sr = parentRecord.SubRecords[idx];
            parentRecord.SubRecords.RemoveAt(idx);
            parentRecord.SubRecords.Insert(idx-1, sr);
            listSubrecord.SelectItem(idx - 1);
            listSubrecord.EnsureVisible(idx - 1);

            MatchRecordStructureToRecord();
            RefreshSelection();
        }

        private void toolStripMoveRecordDown_Click(object sender, EventArgs e)
        {
            if (listSubrecord.SelectedIndices.Count != 1) return;
            int idx = listSubrecord.SelectedIndices[0];
            if (idx < 0 || idx >= (listSubrecord.Items.Count-1))
                return;

            SubRecord sr = parentRecord.SubRecords[idx];
            parentRecord.SubRecords.RemoveAt(idx);
            parentRecord.SubRecords.Insert(idx + 1, sr);
            listSubrecord.SelectItem(idx + 1);
            listSubrecord.EnsureVisible(idx + 1);

            MatchRecordStructureToRecord();
            RefreshSelection();
        }

        #endregion

        private void toolStripEditSubrecord_Click(object sender, EventArgs e)
        {
            EditSelectedSubrecord();
        }

        private void listSubrecord_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            EditSelectedSubrecord();
        }

        private void toolStripEditSubrecordHex_Click(object sender, EventArgs e)
        {
            EditSelectedSubrecordHex();
        }
    }
}