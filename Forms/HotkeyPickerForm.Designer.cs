namespace PersianKeyboardConverter.Forms
{
    partial class HotkeyPickerForm
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
            labelInstruction = new Label();
            labelCurrent = new Label();
            buttonOk = new Button();
            buttonCancel = new Button();
            SuspendLayout();
            // 
            // labelInstruction
            // 
            labelInstruction.Location = new Point(16, 16);
            labelInstruction.Name = "labelInstruction";
            labelInstruction.Size = new Size(320, 20);
            labelInstruction.TabIndex = 0;
            labelInstruction.Text = "Press any key (with Ctrl/Alt/Shift if desired):";
            // 
            // labelCurrent
            // 
            labelCurrent.BorderStyle = BorderStyle.FixedSingle;
            labelCurrent.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            labelCurrent.ForeColor = Color.DarkSlateBlue;
            labelCurrent.Location = new Point(16, 48);
            labelCurrent.Name = "labelCurrent";
            labelCurrent.Size = new Size(320, 30);
            labelCurrent.TabIndex = 1;
            labelCurrent.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // buttonOk
            // 
            buttonOk.DialogResult = DialogResult.OK;
            buttonOk.Location = new Point(16, 100);
            buttonOk.Name = "buttonOk";
            buttonOk.Size = new Size(80, 30);
            buttonOk.TabIndex = 2;
            buttonOk.Text = "OK";
            buttonOk.UseVisualStyleBackColor = true;
            // 
            // buttonCancel
            // 
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.Location = new Point(108, 100);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(80, 30);
            buttonCancel.TabIndex = 3;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            // 
            // HotkeyPickerForm
            // 
            AcceptButton = buttonOk;
            CancelButton = buttonCancel;
            ClientSize = new Size(344, 141);
            Controls.Add(labelInstruction);
            Controls.Add(labelCurrent);
            Controls.Add(buttonOk);
            Controls.Add(buttonCancel);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            KeyPreview = true;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "HotkeyPickerForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Change Hotkey";
            KeyDown += HotkeyPickerForm_KeyDown;
            ResumeLayout(false);

        }
        #endregion

        private System.Windows.Forms.Label labelInstruction = null!;
        private System.Windows.Forms.Label labelCurrent = null!;
        private System.Windows.Forms.Button buttonOk = null!;
        private System.Windows.Forms.Button buttonCancel = null!;
    }
}