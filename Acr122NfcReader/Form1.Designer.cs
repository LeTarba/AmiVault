namespace Acr122NfcReader;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        menuStripMain = new MenuStrip();
        menuItemSettings = new ToolStripMenuItem();
        menuItemAbout = new ToolStripMenuItem();
        toolTipAmiibo = new ToolTip(components);
        groupBoxReader = new GroupBox();
        lblReader = new Label();
        cbReaders = new ComboBox();
        btnRefreshReaders = new Button();
        groupBoxLibrary = new GroupBox();
        btnRefreshLibrary = new Button();
        cbAmiiboFolders = new ComboBox();
        pnlAmiiboLibrary = new FlowLayoutPanel();
        picAmiiboImage = new PictureBox();
        lblAmiiboSelectedName = new Label();
        lblAmiiboInfo = new Label();
        txtAmiiboInfo = new TextBox();
        groupBoxTag = new GroupBox();
        lblVerdictValue = new Label();
        lblVerdictTitle = new Label();
        lblWritableValue = new Label();
        lblWritableTitle = new Label();
        lblBlankValue = new Label();
        lblBlankTitle = new Label();
        lblAtrValue = new Label();
        lblAtrTitle = new Label();
        lblUidValue = new Label();
        lblUidTitle = new Label();
        lblStatusValue = new Label();
        lblStatusTitle = new Label();
        groupBoxActions = new GroupBox();
        btnDump = new Button();
        btnResetTag = new Button();
        btnWriteTest = new Button();
        btnWriteAmiibo = new Button();
        chkAmiiboDryRun = new CheckBox();
        txtLog = new TextBox();
        lblLog = new Label();
        groupBoxLog = new GroupBox();
        ((System.ComponentModel.ISupportInitialize)picAmiiboImage).BeginInit();
        menuStripMain.SuspendLayout();
        groupBoxReader.SuspendLayout();
        groupBoxLibrary.SuspendLayout();
        groupBoxTag.SuspendLayout();
        groupBoxActions.SuspendLayout();
        groupBoxLog.SuspendLayout();
        pollTimer = new System.Windows.Forms.Timer(components);
        SuspendLayout();
        // 
        // menuStripMain
        // 
        menuStripMain.Items.AddRange(new ToolStripItem[] { menuItemSettings, menuItemAbout });
        menuStripMain.Location = new Point(0, 0);
        menuStripMain.Name = "menuStripMain";
        menuStripMain.Size = new Size(1168, 24);
        menuStripMain.TabIndex = 0;
        menuStripMain.Text = "menuStripMain";
        // 
        // menuItemSettings
        // 
        menuItemSettings.Name = "menuItemSettings";
        menuItemSettings.Size = new Size(86, 20);
        menuItemSettings.Text = "Paramètres";
        menuItemSettings.Click += menuItemSettings_Click;
        // 
        // menuItemAbout
        // 
        menuItemAbout.Name = "menuItemAbout";
        menuItemAbout.Size = new Size(67, 20);
        menuItemAbout.Text = "À propos";
        menuItemAbout.Click += menuItemAbout_Click;
        // 
        // groupBoxReader
        // 
        groupBoxReader.Controls.Add(lblReader);
        groupBoxReader.Controls.Add(cbReaders);
        groupBoxReader.Controls.Add(btnRefreshReaders);
        groupBoxReader.Location = new Point(18, 36);
        groupBoxReader.Name = "groupBoxReader";
        groupBoxReader.Size = new Size(700, 62);
        groupBoxReader.TabIndex = 0;
        groupBoxReader.TabStop = false;
        groupBoxReader.Text = "Lecteur";
        // 
        // lblReader
        // 
        lblReader.AutoSize = true;
        lblReader.Location = new Point(16, 29);
        lblReader.Name = "lblReader";
        lblReader.Size = new Size(103, 15);
        lblReader.TabIndex = 0;
        lblReader.Text = "Lecteur NFC/PCSC";
        // 
        // cbReaders
        // 
        cbReaders.DropDownStyle = ComboBoxStyle.DropDownList;
        cbReaders.FormattingEnabled = true;
        cbReaders.Location = new Point(128, 25);
        cbReaders.Name = "cbReaders";
        cbReaders.Size = new Size(398, 23);
        cbReaders.TabIndex = 1;
        // 
        // btnRefreshReaders
        // 
        btnRefreshReaders.Location = new Point(542, 24);
        btnRefreshReaders.Name = "btnRefreshReaders";
        btnRefreshReaders.Size = new Size(96, 25);
        btnRefreshReaders.TabIndex = 2;
        btnRefreshReaders.Text = "Rafraîchir";
        btnRefreshReaders.UseVisualStyleBackColor = true;
        btnRefreshReaders.Click += btnRefreshReaders_Click;
        // 
        // groupBoxLibrary
        // 
        groupBoxLibrary.Controls.Add(btnRefreshLibrary);
        groupBoxLibrary.Controls.Add(cbAmiiboFolders);
        groupBoxLibrary.Controls.Add(pnlAmiiboLibrary);
        groupBoxLibrary.Location = new Point(18, 112);
        groupBoxLibrary.Name = "groupBoxLibrary";
        groupBoxLibrary.Size = new Size(700, 540);
        groupBoxLibrary.TabIndex = 2;
        groupBoxLibrary.TabStop = false;
        groupBoxLibrary.Text = "Bibliothèque amiibo";
        // 
        // btnRefreshLibrary
        // 
        btnRefreshLibrary.Location = new Point(574, 20);
        btnRefreshLibrary.Name = "btnRefreshLibrary";
        btnRefreshLibrary.Size = new Size(110, 25);
        btnRefreshLibrary.TabIndex = 1;
        btnRefreshLibrary.Text = "Rafraîchir";
        btnRefreshLibrary.UseVisualStyleBackColor = true;
        btnRefreshLibrary.Click += btnRefreshLibrary_Click;
        // 
        // cbAmiiboFolders
        // 
        cbAmiiboFolders.DropDownStyle = ComboBoxStyle.DropDownList;
        cbAmiiboFolders.FormattingEnabled = true;
        cbAmiiboFolders.Location = new Point(16, 22);
        cbAmiiboFolders.Name = "cbAmiiboFolders";
        cbAmiiboFolders.Size = new Size(548, 23);
        cbAmiiboFolders.TabIndex = 2;
        cbAmiiboFolders.SelectedIndexChanged += cbAmiiboFolders_SelectedIndexChanged;
        // 
        // pnlAmiiboLibrary
        // 
        pnlAmiiboLibrary.AutoScroll = true;
        pnlAmiiboLibrary.FlowDirection = FlowDirection.LeftToRight;
        pnlAmiiboLibrary.Location = new Point(16, 56);
        pnlAmiiboLibrary.Name = "pnlAmiiboLibrary";
        pnlAmiiboLibrary.Padding = new Padding(8);
        pnlAmiiboLibrary.Size = new Size(668, 468);
        pnlAmiiboLibrary.TabIndex = 3;
        pnlAmiiboLibrary.WrapContents = true;
        // 
        // groupBoxTag
        // 
        groupBoxTag.Controls.Add(lblVerdictValue);
        groupBoxTag.Controls.Add(lblVerdictTitle);
        groupBoxTag.Controls.Add(lblWritableValue);
        groupBoxTag.Controls.Add(lblWritableTitle);
        groupBoxTag.Controls.Add(lblBlankValue);
        groupBoxTag.Controls.Add(lblBlankTitle);
        groupBoxTag.Controls.Add(lblAtrValue);
        groupBoxTag.Controls.Add(lblAtrTitle);
        groupBoxTag.Controls.Add(lblUidValue);
        groupBoxTag.Controls.Add(lblUidTitle);
        groupBoxTag.Controls.Add(lblStatusValue);
        groupBoxTag.Controls.Add(lblStatusTitle);
        groupBoxTag.Controls.Add(picAmiiboImage);
        groupBoxTag.Controls.Add(lblAmiiboSelectedName);
        groupBoxTag.Controls.Add(lblAmiiboInfo);
        groupBoxTag.Controls.Add(txtAmiiboInfo);
        groupBoxTag.Location = new Point(734, 36);
        groupBoxTag.Name = "groupBoxTag";
        groupBoxTag.Size = new Size(416, 486);
        groupBoxTag.TabIndex = 3;
        groupBoxTag.TabStop = false;
        groupBoxTag.Text = "Dernière lecture";
        // 
        // lblVerdictValue
        // 
        lblVerdictValue.AutoEllipsis = true;
        lblVerdictValue.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
        lblVerdictValue.Location = new Point(96, 50);
        lblVerdictValue.Name = "lblVerdictValue";
        lblVerdictValue.Size = new Size(302, 42);
        lblVerdictValue.TabIndex = 11;
        lblVerdictValue.Text = "-";
        // 
        // lblVerdictTitle
        // 
        lblVerdictTitle.AutoSize = true;
        lblVerdictTitle.Location = new Point(18, 50);
        lblVerdictTitle.Name = "lblVerdictTitle";
        lblVerdictTitle.Size = new Size(49, 15);
        lblVerdictTitle.TabIndex = 10;
        lblVerdictTitle.Text = "Verdict :";
        // 
        // lblWritableValue
        // 
        lblWritableValue.AutoEllipsis = true;
        lblWritableValue.Location = new Point(96, 221);
        lblWritableValue.Name = "lblWritableValue";
        lblWritableValue.Size = new Size(302, 23);
        lblWritableValue.TabIndex = 13;
        lblWritableValue.Text = "-";
        // 
        // lblWritableTitle
        // 
        lblWritableTitle.AutoSize = true;
        lblWritableTitle.Location = new Point(18, 221);
        lblWritableTitle.Name = "lblWritableTitle";
        lblWritableTitle.Size = new Size(50, 15);
        lblWritableTitle.TabIndex = 12;
        lblWritableTitle.Text = "Écriture :";
        // 
        // lblBlankValue
        // 
        lblBlankValue.AutoEllipsis = true;
        lblBlankValue.Location = new Point(96, 189);
        lblBlankValue.Name = "lblBlankValue";
        lblBlankValue.Size = new Size(302, 23);
        lblBlankValue.TabIndex = 9;
        lblBlankValue.Text = "-";
        // 
        // lblBlankTitle
        // 
        lblBlankTitle.AutoSize = true;
        lblBlankTitle.Location = new Point(18, 189);
        lblBlankTitle.Name = "lblBlankTitle";
        lblBlankTitle.Size = new Size(46, 15);
        lblBlankTitle.TabIndex = 8;
        lblBlankTitle.Text = "Vierge :";
        // 
        // lblAtrValue
        // 
        lblAtrValue.AutoEllipsis = true;
        lblAtrValue.Location = new Point(96, 147);
        lblAtrValue.Name = "lblAtrValue";
        lblAtrValue.Size = new Size(302, 34);
        lblAtrValue.TabIndex = 7;
        lblAtrValue.Text = "-";
        // 
        // lblAtrTitle
        // 
        lblAtrTitle.AutoSize = true;
        lblAtrTitle.Location = new Point(18, 147);
        lblAtrTitle.Name = "lblAtrTitle";
        lblAtrTitle.Size = new Size(29, 15);
        lblAtrTitle.TabIndex = 6;
        lblAtrTitle.Text = "ATR:";
        // 
        // lblUidValue
        // 
        lblUidValue.AutoSize = true;
        lblUidValue.Font = new Font("Segoe UI", 15.75F, FontStyle.Bold, GraphicsUnit.Point, 0);
        lblUidValue.Location = new Point(96, 101);
        lblUidValue.Name = "lblUidValue";
        lblUidValue.Size = new Size(20, 30);
        lblUidValue.TabIndex = 5;
        lblUidValue.Text = "-";
        // 
        // lblUidTitle
        // 
        lblUidTitle.AutoSize = true;
        lblUidTitle.Location = new Point(18, 109);
        lblUidTitle.Name = "lblUidTitle";
        lblUidTitle.Size = new Size(31, 15);
        lblUidTitle.TabIndex = 4;
        lblUidTitle.Text = "UID :";
        // 
        // lblStatusValue
        // 
        lblStatusValue.AutoEllipsis = true;
        lblStatusValue.Location = new Point(96, 24);
        lblStatusValue.Name = "lblStatusValue";
        lblStatusValue.Size = new Size(302, 23);
        lblStatusValue.TabIndex = 1;
        lblStatusValue.Text = "-";
        // 
        // lblStatusTitle
        // 
        lblStatusTitle.AutoSize = true;
        lblStatusTitle.Location = new Point(18, 24);
        lblStatusTitle.Name = "lblStatusTitle";
        lblStatusTitle.Size = new Size(42, 15);
        lblStatusTitle.TabIndex = 0;
        lblStatusTitle.Text = "Statut :";
        // 
        // picAmiiboImage
        // 
        picAmiiboImage.BorderStyle = BorderStyle.FixedSingle;
        picAmiiboImage.Location = new Point(18, 255);
        picAmiiboImage.Name = "picAmiiboImage";
        picAmiiboImage.Size = new Size(84, 84);
        picAmiiboImage.SizeMode = PictureBoxSizeMode.Zoom;
        picAmiiboImage.TabIndex = 14;
        picAmiiboImage.TabStop = false;
        // 
        // lblAmiiboSelectedName
        // 
        lblAmiiboSelectedName.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point, 0);
        lblAmiiboSelectedName.Location = new Point(112, 255);
        lblAmiiboSelectedName.Name = "lblAmiiboSelectedName";
        lblAmiiboSelectedName.Size = new Size(286, 24);
        lblAmiiboSelectedName.TabIndex = 15;
        lblAmiiboSelectedName.Text = "Aucun amiibo sélectionné";
        // 
        // lblAmiiboInfo
        // 
        lblAmiiboInfo.AutoSize = true;
        lblAmiiboInfo.Location = new Point(112, 284);
        lblAmiiboInfo.Name = "lblAmiiboInfo";
        lblAmiiboInfo.Size = new Size(85, 15);
        lblAmiiboInfo.TabIndex = 16;
        lblAmiiboInfo.Text = "Tag détecté / API";
        // 
        // txtAmiiboInfo
        // 
        txtAmiiboInfo.Location = new Point(112, 304);
        txtAmiiboInfo.Multiline = true;
        txtAmiiboInfo.Name = "txtAmiiboInfo";
        txtAmiiboInfo.ReadOnly = true;
        txtAmiiboInfo.ScrollBars = ScrollBars.Vertical;
        txtAmiiboInfo.Size = new Size(286, 156);
        txtAmiiboInfo.TabIndex = 17;
        // 
        // groupBoxActions
        // 
        groupBoxActions.Controls.Add(btnDump);
        groupBoxActions.Controls.Add(btnResetTag);
        groupBoxActions.Controls.Add(btnWriteTest);
        groupBoxActions.Controls.Add(btnWriteAmiibo);
        groupBoxActions.Controls.Add(chkAmiiboDryRun);
        groupBoxActions.Location = new Point(734, 534);
        groupBoxActions.Name = "groupBoxActions";
        groupBoxActions.Size = new Size(416, 118);
        groupBoxActions.TabIndex = 4;
        groupBoxActions.TabStop = false;
        groupBoxActions.Text = "Actions";
        // 
        // btnDump
        // 
        btnDump.Location = new Point(18, 26);
        btnDump.Name = "btnDump";
        btnDump.Size = new Size(180, 26);
        btnDump.TabIndex = 0;
        btnDump.Text = "Dumper le tag";
        btnDump.UseVisualStyleBackColor = true;
        btnDump.Click += btnDump_Click;
        // 
        // btnResetTag
        // 
        btnResetTag.Location = new Point(216, 26);
        btnResetTag.Name = "btnResetTag";
        btnResetTag.Size = new Size(180, 26);
        btnResetTag.TabIndex = 1;
        btnResetTag.Text = "Formater / remise à zéro";
        btnResetTag.UseVisualStyleBackColor = true;
        btnResetTag.Click += btnResetTag_Click;
        // 
        // btnWriteTest
        // 
        btnWriteTest.Location = new Point(18, 58);
        btnWriteTest.Name = "btnWriteTest";
        btnWriteTest.Size = new Size(180, 26);
        btnWriteTest.TabIndex = 2;
        btnWriteTest.Text = "Tester l'écriture";
        btnWriteTest.UseVisualStyleBackColor = true;
        btnWriteTest.Click += btnWriteTest_Click;
        // 
        // btnWriteAmiibo
        // 
        btnWriteAmiibo.Location = new Point(216, 58);
        btnWriteAmiibo.Name = "btnWriteAmiibo";
        btnWriteAmiibo.Size = new Size(180, 26);
        btnWriteAmiibo.TabIndex = 3;
        btnWriteAmiibo.Text = "Écrire un amiibo .bin";
        btnWriteAmiibo.UseVisualStyleBackColor = true;
        btnWriteAmiibo.Click += btnWriteAmiibo_Click;
        // 
        // chkAmiiboDryRun
        // 
        chkAmiiboDryRun.AutoSize = true;
        chkAmiiboDryRun.Location = new Point(18, 88);
        chkAmiiboDryRun.Name = "chkAmiiboDryRun";
        chkAmiiboDryRun.Size = new Size(234, 19);
        chkAmiiboDryRun.TabIndex = 4;
        chkAmiiboDryRun.Text = "Mode dry run non destructif";
        chkAmiiboDryRun.UseVisualStyleBackColor = true;
        chkAmiiboDryRun.CheckedChanged += chkAmiiboDryRun_CheckedChanged;
        // 
        // groupBoxLog
        // 
        groupBoxLog.Controls.Add(lblLog);
        groupBoxLog.Controls.Add(txtLog);
        groupBoxLog.Location = new Point(18, 668);
        groupBoxLog.Name = "groupBoxLog";
        groupBoxLog.Size = new Size(1132, 194);
        groupBoxLog.TabIndex = 5;
        groupBoxLog.TabStop = false;
        groupBoxLog.Text = "Journal";
        // 
        // txtLog
        // 
        txtLog.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
        txtLog.Location = new Point(16, 41);
        txtLog.Multiline = true;
        txtLog.Name = "txtLog";
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.Size = new Size(1100, 136);
        txtLog.TabIndex = 1;
        // 
        // lblLog
        // 
        lblLog.AutoSize = true;
        lblLog.Location = new Point(16, 22);
        lblLog.Name = "lblLog";
        lblLog.Size = new Size(120, 15);
        lblLog.TabIndex = 0;
        lblLog.Text = "Journal des opérations";
        // 
        // pollTimer
        // 
        pollTimer.Interval = 1000;
        pollTimer.Tick += pollTimer_Tick;
        // 
        // Form1
        // 
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1168, 870);
        Controls.Add(groupBoxLog);
        Controls.Add(groupBoxActions);
        Controls.Add(groupBoxTag);
        Controls.Add(groupBoxLibrary);
        Controls.Add(groupBoxReader);
        Controls.Add(menuStripMain);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MainMenuStrip = menuStripMain;
        MaximizeBox = false;
        Name = "Form1";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "AmiVault";
        FormClosing += Form1_FormClosing;
        Load += Form1_Load;
        menuStripMain.ResumeLayout(false);
        menuStripMain.PerformLayout();
        groupBoxReader.ResumeLayout(false);
        groupBoxReader.PerformLayout();
        groupBoxLibrary.ResumeLayout(false);
        groupBoxLibrary.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)picAmiiboImage).EndInit();
        groupBoxTag.ResumeLayout(false);
        groupBoxTag.PerformLayout();
        groupBoxActions.ResumeLayout(false);
        groupBoxActions.PerformLayout();
        groupBoxLog.ResumeLayout(false);
        groupBoxLog.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private MenuStrip menuStripMain;
    private ToolStripMenuItem menuItemSettings;
    private ToolStripMenuItem menuItemAbout;
    private GroupBox groupBoxReader;
    private Label lblReader;
    private ComboBox cbReaders;
    private Button btnRefreshReaders;
    private GroupBox groupBoxLibrary;
    private Button btnRefreshLibrary;
    private ComboBox cbAmiiboFolders;
    private FlowLayoutPanel pnlAmiiboLibrary;
    private PictureBox picAmiiboImage;
    private Label lblAmiiboSelectedName;
    private Label lblAmiiboInfo;
    private TextBox txtAmiiboInfo;
    private GroupBox groupBoxTag;
    private Label lblVerdictValue;
    private Label lblVerdictTitle;
    private Label lblWritableValue;
    private Label lblWritableTitle;
    private Label lblBlankValue;
    private Label lblBlankTitle;
    private Label lblAtrValue;
    private Label lblAtrTitle;
    private Label lblUidValue;
    private Label lblUidTitle;
    private Label lblStatusValue;
    private Label lblStatusTitle;
    private GroupBox groupBoxActions;
    private Button btnDump;
    private Button btnResetTag;
    private Button btnWriteTest;
    private Button btnWriteAmiibo;
    private CheckBox chkAmiiboDryRun;
    private TextBox txtLog;
    private Label lblLog;
    private GroupBox groupBoxLog;
    private ToolTip toolTipAmiibo;
    private System.Windows.Forms.Timer pollTimer;
}
