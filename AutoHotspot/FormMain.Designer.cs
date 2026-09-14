namespace AutoHotspot;

partial class FormMain
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
        labelIntro = new Label();
        toggleRowEnabled = new ToggleRow();
        groupBoxStatus = new GroupBox();
        buttonStartKeepAlive = new Button();
        labelKeepAliveValue = new Label();
        labelKeepAliveCaption = new Label();
        labelHotspotValue = new Label();
        labelHotspotCaption = new Label();
        labelNote = new Label();
        linkLabelHotspotSettings = new LinkLabel();
        linkLabelOpenLog = new LinkLabel();
        buttonClose = new Button();
        timerStatus = new System.Windows.Forms.Timer(components);
        groupBoxStatus.SuspendLayout();
        SuspendLayout();
        //
        // labelIntro
        //
        labelIntro.Location = new Point(16, 14);
        labelIntro.Name = "labelIntro";
        labelIntro.Size = new Size(468, 96);
        labelIntro.TabIndex = 0;
        labelIntro.Text = "AutoHotspot keeps the Windows Mobile hotspot switched on.\r\n\r\nWhen enabled, it starts every time you sign in to Windows, turns Wi-Fi on if it is off, and keeps retrying until the hotspot is running. If Windows switches the hotspot off later, AutoHotspot turns it back on within a few seconds.";
        //
        // toggleRowEnabled
        //
        toggleRowEnabled.Location = new Point(16, 118);
        toggleRowEnabled.Name = "toggleRowEnabled";
        toggleRowEnabled.OffSubtitle = "Click to start the hotspot automatically at every sign-in.";
        toggleRowEnabled.OffTitle = "Disabled";
        toggleRowEnabled.OnSubtitle = "Starts at sign-in and keeps the hotspot on. Click to disable.";
        toggleRowEnabled.OnTitle = "Enabled";
        toggleRowEnabled.Size = new Size(468, 64);
        toggleRowEnabled.TabIndex = 1;
        toggleRowEnabled.CheckedChanged += toggleRowEnabled_CheckedChanged;
        //
        // groupBoxStatus
        //
        groupBoxStatus.Controls.Add(buttonStartKeepAlive);
        groupBoxStatus.Controls.Add(labelKeepAliveValue);
        groupBoxStatus.Controls.Add(labelKeepAliveCaption);
        groupBoxStatus.Controls.Add(labelHotspotValue);
        groupBoxStatus.Controls.Add(labelHotspotCaption);
        groupBoxStatus.Location = new Point(16, 194);
        groupBoxStatus.Name = "groupBoxStatus";
        groupBoxStatus.Size = new Size(468, 88);
        groupBoxStatus.TabIndex = 2;
        groupBoxStatus.TabStop = false;
        groupBoxStatus.Text = "Status";
        //
        // buttonStartKeepAlive
        //
        buttonStartKeepAlive.Location = new Point(324, 50);
        buttonStartKeepAlive.Name = "buttonStartKeepAlive";
        buttonStartKeepAlive.Size = new Size(132, 27);
        buttonStartKeepAlive.TabIndex = 4;
        buttonStartKeepAlive.Text = "Start now";
        buttonStartKeepAlive.UseVisualStyleBackColor = true;
        buttonStartKeepAlive.Visible = false;
        buttonStartKeepAlive.Click += buttonStartKeepAlive_Click;
        //
        // labelKeepAliveValue
        //
        labelKeepAliveValue.AutoSize = true;
        labelKeepAliveValue.Location = new Point(130, 55);
        labelKeepAliveValue.Name = "labelKeepAliveValue";
        labelKeepAliveValue.Size = new Size(12, 15);
        labelKeepAliveValue.TabIndex = 3;
        labelKeepAliveValue.Text = "-";
        //
        // labelKeepAliveCaption
        //
        labelKeepAliveCaption.AutoSize = true;
        labelKeepAliveCaption.Location = new Point(12, 55);
        labelKeepAliveCaption.Name = "labelKeepAliveCaption";
        labelKeepAliveCaption.Size = new Size(92, 15);
        labelKeepAliveCaption.TabIndex = 2;
        labelKeepAliveCaption.Text = "Background:";
        //
        // labelHotspotValue
        //
        labelHotspotValue.AutoSize = true;
        labelHotspotValue.Location = new Point(130, 26);
        labelHotspotValue.Name = "labelHotspotValue";
        labelHotspotValue.Size = new Size(12, 15);
        labelHotspotValue.TabIndex = 1;
        labelHotspotValue.Text = "-";
        //
        // labelHotspotCaption
        //
        labelHotspotCaption.AutoSize = true;
        labelHotspotCaption.Location = new Point(12, 26);
        labelHotspotCaption.Name = "labelHotspotCaption";
        labelHotspotCaption.Size = new Size(97, 15);
        labelHotspotCaption.TabIndex = 0;
        labelHotspotCaption.Text = "Mobile hotspot:";
        //
        // labelNote
        //
        labelNote.ForeColor = SystemColors.GrayText;
        labelNote.Location = new Point(16, 292);
        labelNote.Name = "labelNote";
        labelNote.Size = new Size(468, 34);
        labelNote.TabIndex = 3;
        labelNote.Text = "Runs when you sign in, not before. The hotspot name and password are set in Windows Settings.";
        //
        // linkLabelHotspotSettings
        //
        linkLabelHotspotSettings.AutoSize = true;
        linkLabelHotspotSettings.Location = new Point(16, 340);
        linkLabelHotspotSettings.Name = "linkLabelHotspotSettings";
        linkLabelHotspotSettings.Size = new Size(172, 15);
        linkLabelHotspotSettings.TabIndex = 4;
        linkLabelHotspotSettings.TabStop = true;
        linkLabelHotspotSettings.Text = "Open Mobile hotspot settings";
        linkLabelHotspotSettings.LinkClicked += linkLabelHotspotSettings_LinkClicked;
        //
        // linkLabelOpenLog
        //
        linkLabelOpenLog.AutoSize = true;
        linkLabelOpenLog.Location = new Point(210, 340);
        linkLabelOpenLog.Name = "linkLabelOpenLog";
        linkLabelOpenLog.Size = new Size(57, 15);
        linkLabelOpenLog.TabIndex = 5;
        linkLabelOpenLog.TabStop = true;
        linkLabelOpenLog.Text = "Open log";
        linkLabelOpenLog.LinkClicked += linkLabelOpenLog_LinkClicked;
        //
        // buttonClose
        //
        buttonClose.DialogResult = DialogResult.Cancel;
        buttonClose.Location = new Point(394, 334);
        buttonClose.Name = "buttonClose";
        buttonClose.Size = new Size(90, 27);
        buttonClose.TabIndex = 6;
        buttonClose.Text = "Close";
        buttonClose.UseVisualStyleBackColor = true;
        buttonClose.Click += buttonClose_Click;
        //
        // timerStatus
        //
        timerStatus.Interval = 2000;
        timerStatus.Tick += timerStatus_Tick;
        //
        // FormMain
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = buttonClose;
        ClientSize = new Size(500, 376);
        Controls.Add(buttonClose);
        Controls.Add(linkLabelOpenLog);
        Controls.Add(linkLabelHotspotSettings);
        Controls.Add(labelNote);
        Controls.Add(groupBoxStatus);
        Controls.Add(toggleRowEnabled);
        Controls.Add(labelIntro);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "FormMain";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "AutoHotspot";
        Load += FormMain_Load;
        groupBoxStatus.ResumeLayout(false);
        groupBoxStatus.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label labelIntro;
    private ToggleRow toggleRowEnabled;
    private GroupBox groupBoxStatus;
    private Label labelHotspotCaption;
    private Label labelHotspotValue;
    private Label labelKeepAliveCaption;
    private Label labelKeepAliveValue;
    private Button buttonStartKeepAlive;
    private Label labelNote;
    private LinkLabel linkLabelHotspotSettings;
    private LinkLabel linkLabelOpenLog;
    private Button buttonClose;
    private System.Windows.Forms.Timer timerStatus;
}
