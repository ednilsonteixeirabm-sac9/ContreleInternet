using System;
using System.Drawing;
using System.Windows.Forms;
using ControleInternet.Common;

namespace ControleInternet
{
    internal sealed class SiteEditForm : Form
    {
        private readonly TextBox _domain;

        public SiteEditForm(string currentDomain)
        {
            Text = string.IsNullOrEmpty(currentDomain) ? "Adicionar site" : "Editar site";
            ClientSize = new Size(420, 135);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9F);

            Label label = new Label
            {
                AutoSize = true,
                Location = new Point(20, 18),
                Text = "Domínio (sem http:// ou https://):"
            };

            _domain = new TextBox
            {
                Location = new Point(20, 43),
                Size = new Size(380, 23),
                Text = currentDomain ?? string.Empty
            };

            Button confirm = new Button
            {
                Location = new Point(300, 88),
                Size = new Size(100, 30),
                Text = "OK"
            };
            confirm.Click += ConfirmClick;

            Button cancel = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(190, 88),
                Size = new Size(100, 30),
                Text = "Cancelar"
            };

            Controls.Add(label);
            Controls.Add(_domain);
            Controls.Add(cancel);
            Controls.Add(confirm);
            AcceptButton = confirm;
            CancelButton = cancel;
            Shown += delegate
            {
                _domain.SelectAll();
                _domain.Focus();
            };
        }

        public string Domain { get; private set; }

        private void ConfirmClick(object sender, EventArgs eventArgs)
        {
            try
            {
                Domain = DomainName.Normalize(_domain.Text);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (FormatException exception)
            {
                MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _domain.Focus();
            }
        }
    }
}
