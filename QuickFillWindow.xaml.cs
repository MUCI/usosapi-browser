using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;

namespace UsosApiBrowser
{
    public partial class QuickFillWindow : Window
    {
        public QuickFillWindow()
        {
            InitializeComponent();
        }

        private void okButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            foreach (ApiScope scope in ((BrowserWindow)this.Owner).scopes)
            {
                var cb = new CheckBox { Content = scope.key, Width = 130 };
                ((BrowserWindow)this.Owner).varsCache.BindWithCheckBox("quickFill#" + scope.key, cb);
                this.scopeCheckboxesPanel.Children.Add(cb);
            }
        }


        internal System.Collections.Generic.List<string> GetSelectedScopeKeys()
        {
            var scopes = new List<string>();
            foreach (CheckBox cb in this.scopeCheckboxesPanel.Children)
            {
                if (cb.IsChecked == true)
                    scopes.Add(cb.Content.ToString());
            }
            return scopes;
        }
    }
}
