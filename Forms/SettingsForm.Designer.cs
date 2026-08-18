namespace PersianKeyboardConverter
{
    partial class SettingsForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SettingsForm));
            groupBoxHotkey = new GroupBox();
            labelCurrentHotkey = new Label();
            buttonChangeHotkey = new Button();
            groupBoxCorrectionHotkey = new GroupBox();
            labelCorrectionHotkey = new Label();
            buttonChangeCorrectionHotkey = new Button();
            groupBoxTranslationHotkey = new GroupBox();
            labelTranslationHotkey = new Label();
            buttonChangeTranslationHotkey = new Button();
            groupBoxOptions = new GroupBox();
            checkBoxEnabled = new CheckBox();
            checkBoxNotifications = new CheckBox();
            checkBoxSwitchLayout = new CheckBox();
            checkBoxAutostart = new CheckBox();
            buttonSave = new Button();
            labelStatus = new Label();
            groupBoxHotkey.SuspendLayout();
            groupBoxCorrectionHotkey.SuspendLayout();
            groupBoxTranslationHotkey.SuspendLayout();
            groupBoxOptions.SuspendLayout();
            SuspendLayout();
            // 
            // groupBoxHotkey
            // 
            groupBoxHotkey.Controls.Add(labelCurrentHotkey);
            groupBoxHotkey.Controls.Add(buttonChangeHotkey);
            groupBoxHotkey.Location = new Point(12, 10);
            groupBoxHotkey.Name = "groupBoxHotkey";
            groupBoxHotkey.Size = new Size(385, 72);
            groupBoxHotkey.TabIndex = 0;
            groupBoxHotkey.TabStop = false;
            groupBoxHotkey.Text = "Global Hotkey";
            // 
            // labelCurrentHotkey
            // 
            labelCurrentHotkey.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            labelCurrentHotkey.ForeColor = Color.DarkSlateBlue;
            labelCurrentHotkey.Location = new Point(12, 26);
            labelCurrentHotkey.Name = "labelCurrentHotkey";
            labelCurrentHotkey.Size = new Size(260, 24);
            labelCurrentHotkey.TabIndex = 0;
            labelCurrentHotkey.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // buttonChangeHotkey
            // 
            buttonChangeHotkey.Location = new Point(280, 24);
            buttonChangeHotkey.Name = "buttonChangeHotkey";
            buttonChangeHotkey.Size = new Size(90, 28);
            buttonChangeHotkey.TabIndex = 1;
            buttonChangeHotkey.Text = "Change…";
            buttonChangeHotkey.UseVisualStyleBackColor = true;
            buttonChangeHotkey.Click += BtnChangeHotkey_Click;
            // 
            // groupBoxCorrectionHotkey
            // 
            groupBoxCorrectionHotkey.Controls.Add(labelCorrectionHotkey);
            groupBoxCorrectionHotkey.Controls.Add(buttonChangeCorrectionHotkey);
            groupBoxCorrectionHotkey.Location = new Point(12, 88);
            groupBoxCorrectionHotkey.Name = "groupBoxCorrectionHotkey";
            groupBoxCorrectionHotkey.Size = new Size(385, 72);
            groupBoxCorrectionHotkey.TabIndex = 1;
            groupBoxCorrectionHotkey.TabStop = false;
            groupBoxCorrectionHotkey.Text = "Correction Hotkey (F9: fix misspelled word)";
            // 
            // labelCorrectionHotkey
            // 
            labelCorrectionHotkey.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            labelCorrectionHotkey.ForeColor = Color.DarkSlateBlue;
            labelCorrectionHotkey.Location = new Point(12, 26);
            labelCorrectionHotkey.Name = "labelCorrectionHotkey";
            labelCorrectionHotkey.Size = new Size(260, 24);
            labelCorrectionHotkey.TabIndex = 0;
            labelCorrectionHotkey.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // buttonChangeCorrectionHotkey
            // 
            buttonChangeCorrectionHotkey.Location = new Point(280, 24);
            buttonChangeCorrectionHotkey.Name = "buttonChangeCorrectionHotkey";
            buttonChangeCorrectionHotkey.Size = new Size(90, 28);
            buttonChangeCorrectionHotkey.TabIndex = 1;
            buttonChangeCorrectionHotkey.Text = "Change…";
            buttonChangeCorrectionHotkey.UseVisualStyleBackColor = true;
            buttonChangeCorrectionHotkey.Click += BtnChangeCorrectionHotkey_Click;
            // 
            // groupBoxTranslationHotkey
            // 
            groupBoxTranslationHotkey.Controls.Add(labelTranslationHotkey);
            groupBoxTranslationHotkey.Controls.Add(buttonChangeTranslationHotkey);
            groupBoxTranslationHotkey.Location = new Point(12, 166);
            groupBoxTranslationHotkey.Name = "groupBoxTranslationHotkey";
            groupBoxTranslationHotkey.Size = new Size(385, 72);
            groupBoxTranslationHotkey.TabIndex = 2;
            groupBoxTranslationHotkey.TabStop = false;
            groupBoxTranslationHotkey.Text = "Translation Hotkey (F8: translate selected text)";
            // 
            // labelTranslationHotkey
            // 
            labelTranslationHotkey.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            labelTranslationHotkey.ForeColor = Color.DarkSlateBlue;
            labelTranslationHotkey.Location = new Point(12, 26);
            labelTranslationHotkey.Name = "labelTranslationHotkey";
            labelTranslationHotkey.Size = new Size(260, 24);
            labelTranslationHotkey.TabIndex = 0;
            labelTranslationHotkey.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // buttonChangeTranslationHotkey
            // 
            buttonChangeTranslationHotkey.Location = new Point(280, 24);
            buttonChangeTranslationHotkey.Name = "buttonChangeTranslationHotkey";
            buttonChangeTranslationHotkey.Size = new Size(90, 28);
            buttonChangeTranslationHotkey.TabIndex = 1;
            buttonChangeTranslationHotkey.Text = "Change…";
            buttonChangeTranslationHotkey.UseVisualStyleBackColor = true;
            buttonChangeTranslationHotkey.Click += BtnChangeTranslationHotkey_Click;
            // 
            // groupBoxOptions
            // 
            groupBoxOptions.Controls.Add(checkBoxEnabled);
            groupBoxOptions.Controls.Add(checkBoxNotifications);
            groupBoxOptions.Controls.Add(checkBoxSwitchLayout);
            groupBoxOptions.Controls.Add(checkBoxAutostart);
            groupBoxOptions.Location = new Point(12, 244);
            groupBoxOptions.Name = "groupBoxOptions";
            groupBoxOptions.Size = new Size(385, 148);
            groupBoxOptions.TabIndex = 3;
            groupBoxOptions.TabStop = false;
            groupBoxOptions.Text = "Options";
            // 
            // checkBoxEnabled
            // 
            checkBoxEnabled.Checked = true;
            checkBoxEnabled.CheckState = CheckState.Checked;
            checkBoxEnabled.Location = new Point(12, 24);
            checkBoxEnabled.Name = "checkBoxEnabled";
            checkBoxEnabled.Size = new Size(340, 22);
            checkBoxEnabled.TabIndex = 0;
            checkBoxEnabled.Text = "Conversion enabled";
            checkBoxEnabled.UseVisualStyleBackColor = true;
            // 
            // checkBoxNotifications
            // 
            checkBoxNotifications.Checked = true;
            checkBoxNotifications.CheckState = CheckState.Checked;
            checkBoxNotifications.Location = new Point(12, 52);
            checkBoxNotifications.Name = "checkBoxNotifications";
            checkBoxNotifications.Size = new Size(340, 22);
            checkBoxNotifications.TabIndex = 1;
            checkBoxNotifications.Text = "Show tray notifications on convert";
            checkBoxNotifications.UseVisualStyleBackColor = true;
            // 
            // checkBoxSwitchLayout
            // 
            checkBoxSwitchLayout.Checked = true;
            checkBoxSwitchLayout.CheckState = CheckState.Checked;
            checkBoxSwitchLayout.Location = new Point(12, 80);
            checkBoxSwitchLayout.Name = "checkBoxSwitchLayout";
            checkBoxSwitchLayout.Size = new Size(340, 22);
            checkBoxSwitchLayout.TabIndex = 2;
            checkBoxSwitchLayout.Text = "Switch keyboard layout to match converted text";
            checkBoxSwitchLayout.UseVisualStyleBackColor = true;
            // 
            // checkBoxAutostart
            // 
            checkBoxAutostart.Location = new Point(12, 108);
            checkBoxAutostart.Name = "checkBoxAutostart";
            checkBoxAutostart.Size = new Size(340, 22);
            checkBoxAutostart.TabIndex = 3;
            checkBoxAutostart.Text = "Start with Windows";
            checkBoxAutostart.UseVisualStyleBackColor = true;
            // 
            // buttonSave
            // 
            buttonSave.Location = new Point(12, 398);
            buttonSave.Name = "buttonSave";
            buttonSave.Size = new Size(120, 32);
            buttonSave.TabIndex = 4;
            buttonSave.Text = "Save Settings";
            buttonSave.UseVisualStyleBackColor = true;
            buttonSave.Click += buttonSave_Click;
            // 
            // labelStatus
            // 
            labelStatus.ForeColor = Color.ForestGreen;
            labelStatus.Location = new Point(148, 404);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(250, 20);
            labelStatus.TabIndex = 5;
            // 
            // SettingsForm
            // 
            ClientSize = new Size(402, 442);
            Controls.Add(groupBoxHotkey);
            Controls.Add(groupBoxCorrectionHotkey);
            Controls.Add(groupBoxTranslationHotkey);
            Controls.Add(groupBoxOptions);
            Controls.Add(buttonSave);
            Controls.Add(labelStatus);
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            MaximizeBox = false;
            Name = "SettingsForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Persian Keyboard Converter — Settings";
            groupBoxHotkey.ResumeLayout(false);
            groupBoxCorrectionHotkey.ResumeLayout(false);
            groupBoxTranslationHotkey.ResumeLayout(false);
            groupBoxOptions.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.GroupBox groupBoxHotkey = null!;
        private System.Windows.Forms.Label labelCurrentHotkey = null!;
        private System.Windows.Forms.Button buttonChangeHotkey = null!;

        private System.Windows.Forms.GroupBox groupBoxCorrectionHotkey = null!;
        private System.Windows.Forms.Label labelCorrectionHotkey = null!;
        private System.Windows.Forms.Button buttonChangeCorrectionHotkey = null!;

        private System.Windows.Forms.GroupBox groupBoxTranslationHotkey = null!;
        private System.Windows.Forms.Label labelTranslationHotkey = null!;
        private System.Windows.Forms.Button buttonChangeTranslationHotkey = null!;

        private System.Windows.Forms.GroupBox groupBoxOptions = null!;
        private System.Windows.Forms.CheckBox checkBoxEnabled = null!;
        private System.Windows.Forms.CheckBox checkBoxNotifications = null!;
        private System.Windows.Forms.CheckBox checkBoxSwitchLayout = null!;
        private System.Windows.Forms.CheckBox checkBoxAutostart = null!;

        private System.Windows.Forms.Button buttonSave = null!;
        private System.Windows.Forms.Label labelStatus = null!;
    }
}