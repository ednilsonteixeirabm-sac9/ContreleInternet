using System;
using System.IO;
using System.Windows.Forms;
using ControleInternet.Common;

namespace ControleInternet
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AdminClient client = new AdminClient();
            try
            {
                AdminResponse status = client.Status();
                if (!status.Success)
                {
                    ShowError(status.Error);
                    return;
                }

                using (LoginForm login = new LoginForm(client, !status.Initialized))
                {
                    if (login.ShowDialog() != DialogResult.OK)
                    {
                        return;
                    }

                    Application.Run(new MainForm(client, login.Password, login.Config));
                }
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
            }
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(
                message,
                "Controle de Internet",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
