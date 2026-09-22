using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ControleInternet.Common;

namespace ControleInternet
{
    internal sealed class MainForm : Form
    {
        private readonly AdminClient _client;
        private readonly string _password;
        private readonly CheckBox _blockAll;
        private readonly CheckBox _allowList;
        private readonly ListBox _sites;
        private readonly Button _add;
        private readonly Button _edit;
        private readonly Button _remove;
        private readonly Button _save;

        public MainForm(AdminClient client, string password, AppConfig config)
        {
            _client = client;
            _password = password;

            Text = "Controle de Internet";
            ClientSize = new Size(590, 460);
            MinimumSize = new Size(500, 420);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);

            _blockAll = new CheckBox
            {
                AutoSize = true,
                Location = new Point(24, 24),
                Text = "Bloquear todos os sites",
                Checked = config.BlockAllSites
            };
            _blockAll.CheckedChanged += delegate { UpdateEnabledState(); };

            _allowList = new CheckBox
            {
                AutoSize = true,
                Location = new Point(24, 60),
                Text = "Liberar os sites listados a seguir",
                Checked = config.AllowListedSites
            };

            _sites = new ListBox
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(24, 96),
                Size = new Size(542, 275),
                IntegralHeight = false,
                Sorted = true
            };
            foreach (string domain in config.AllowedDomains ?? new List<string>())
            {
                _sites.Items.Add(domain);
            }
            _sites.DoubleClick += delegate { EditSite(); };
            _sites.SelectedIndexChanged += delegate { UpdateEnabledState(); };

            _add = CreateButton("Adicionar", 24);
            _edit = CreateButton("Editar", 134);
            _remove = CreateButton("Remover", 244);
            _add.Click += delegate { AddSite(); };
            _edit.Click += delegate { EditSite(); };
            _remove.Click += delegate { RemoveSite(); };

            _save = new Button
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(456, 404),
                Size = new Size(110, 32),
                Text = "Salvar"
            };
            _save.Click += SaveClick;

            Controls.Add(_blockAll);
            Controls.Add(_allowList);
            Controls.Add(_sites);
            Controls.Add(_add);
            Controls.Add(_edit);
            Controls.Add(_remove);
            Controls.Add(_save);
            UpdateEnabledState();
        }

        private Button CreateButton(string text, int left)
        {
            return new Button
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Location = new Point(left, 404),
                Size = new Size(100, 32),
                Text = text
            };
        }

        private void UpdateEnabledState()
        {
            bool listEnabled = _blockAll.Checked;
            _allowList.Enabled = listEnabled;
            _sites.Enabled = listEnabled;
            _add.Enabled = listEnabled;
            _edit.Enabled = listEnabled && _sites.SelectedIndex >= 0;
            _remove.Enabled = listEnabled && _sites.SelectedIndex >= 0;
        }

        private void AddSite()
        {
            using (SiteEditForm dialog = new SiteEditForm(null))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (ContainsDomain(dialog.Domain, -1))
                    {
                        ShowWarning("Esse domínio já está na lista.");
                        return;
                    }

                    _sites.Items.Add(dialog.Domain);
                    _sites.SelectedItem = dialog.Domain;
                }
            }
        }

        private void EditSite()
        {
            int index = _sites.SelectedIndex;
            if (index < 0)
            {
                return;
            }

            using (SiteEditForm dialog = new SiteEditForm((string)_sites.Items[index]))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (ContainsDomain(dialog.Domain, index))
                    {
                        ShowWarning("Esse domínio já está na lista.");
                        return;
                    }

                    _sites.Items[index] = dialog.Domain;
                    _sites.SelectedItem = dialog.Domain;
                }
            }
        }

        private void RemoveSite()
        {
            int index = _sites.SelectedIndex;
            if (index < 0)
            {
                return;
            }

            string domain = (string)_sites.Items[index];
            if (MessageBox.Show(
                    this,
                    "Remover \"" + domain + "\" da lista?",
                    Text,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _sites.Items.RemoveAt(index);
            }
        }

        private bool ContainsDomain(string domain, int ignoredIndex)
        {
            for (int index = 0; index < _sites.Items.Count; index++)
            {
                if (index != ignoredIndex
                    && string.Equals((string)_sites.Items[index], domain, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void SaveClick(object sender, EventArgs eventArgs)
        {
            _save.Enabled = false;
            try
            {
                AppConfig config = new AppConfig
                {
                    BlockAllSites = _blockAll.Checked,
                    AllowListedSites = _allowList.Checked,
                    AllowedDomains = _sites.Items.Cast<string>().ToList()
                };

                AdminResponse response = _client.Save(_password, config);
                if (!response.Success)
                {
                    ShowWarning(response.Error);
                    return;
                }

                WinInetRefresh.NotifyCurrentSession();
                MessageBox.Show(
                    this,
                    "Configurações salvas e enviadas ao serviço.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                ShowWarning(exception.Message);
            }
            finally
            {
                _save.Enabled = true;
            }
        }

        private void ShowWarning(string message)
        {
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
