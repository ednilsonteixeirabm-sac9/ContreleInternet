using System;
using System.Drawing;
using System.Windows.Forms;
using ControleInternet.Common;

namespace ControleInternet
{
    internal sealed class LoginForm : Form
    {
        private readonly AdminClient _client;
        private readonly bool _createPassword;
        private readonly TextBox _password;
        private readonly TextBox _confirmation;
        private readonly Button _continue;

        public LoginForm(AdminClient client, bool createPassword)
        {
            _client = client;
            _createPassword = createPassword;

            Text = createPassword ? "Criar senha — Controle de Internet" : "Acesso — Controle de Internet";
            ClientSize = new Size(390, createPassword ? 190 : 145);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);

            Label explanation = new Label
            {
                AutoSize = false,
                Location = new Point(20, 15),
                Size = new Size(350, 35),
                Text = createPassword
                    ? "Primeiro acesso: crie a senha do Controle de Internet."
                    : "Informe a senha do Controle de Internet."
            };

            Label passwordLabel = new Label
            {
                AutoSize = true,
                Location = new Point(20, 58),
                Text = "Senha:"
            };

            _password = new TextBox
            {
                Location = new Point(115, 54),
                Size = new Size(255, 23),
                UseSystemPasswordChar = true
            };

            _confirmation = new TextBox
            {
                Location = new Point(115, 88),
                Size = new Size(255, 23),
                UseSystemPasswordChar = true,
                Visible = createPassword
            };

            Label confirmationLabel = new Label
            {
                AutoSize = true,
                Location = new Point(20, 92),
                Text = "Confirmar:",
                Visible = createPassword
            };

            _continue = new Button
            {
                Location = new Point(270, createPassword ? 135 : 93),
                Size = new Size(100, 30),
                Text = "Continuar"
            };
            _continue.Click += ContinueClick;

            Button cancel = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(160, createPassword ? 135 : 93),
                Size = new Size(100, 30),
                Text = "Cancelar"
            };

            Controls.Add(explanation);
            Controls.Add(passwordLabel);
            Controls.Add(_password);
            Controls.Add(confirmationLabel);
            Controls.Add(_confirmation);
            Controls.Add(cancel);
            Controls.Add(_continue);

            AcceptButton = _continue;
            CancelButton = cancel;
            Shown += delegate { _password.Focus(); };
        }

        public string Password { get; private set; }

        public AppConfig Config { get; private set; }

        private void ContinueClick(object sender, EventArgs eventArgs)
        {
            try
            {
                AdminResponse response;
                if (_createPassword)
                {
                    if (!string.Equals(_password.Text, _confirmation.Text, StringComparison.Ordinal))
                    {
                        ShowError("A confirmação da senha não confere.");
                        return;
                    }

                    PasswordHasher.ValidatePassword(_password.Text);
                    response = _client.Initialize(_password.Text);
                }
                else
                {
                    response = _client.Authenticate(_password.Text);
                }

                if (!response.Success)
                {
                    ShowError(response.Error);
                    _password.SelectAll();
                    _password.Focus();
                    return;
                }

                Password = _password.Text;
                Config = response.Config ?? new AppConfig();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
            }
        }

        private void ShowError(string message)
        {
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
