using System;
using System.Collections.Generic;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Newtonsoft.Json;
using System.Reflection;
using System.Diagnostics;

namespace UsosApiBrowser
{
    public partial class BrowserWindow : Window
    {
        /// <summary>
        /// This holds cached information on the data user is filling into various
        /// inputs. Makes it a bit easier for a user to not get annoyed with this app.
        /// </summary>
        public VarsCache varsCache = new VarsCache();

        /// <summary>
        /// Used to connect to USOS API installations.
        /// </summary>
        private ApiConnector apiConnector;

        public BrowserWindow()
        {
            InitializeComponent();
            MessageBox.Show("Please note, that this is a development tool only and it might be a bit buggy. " +
                "However, this stands for this client application only, not the USOS API itself!",
                "USOS API Browser", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (System.Deployment.Application.ApplicationDeployment.IsNetworkDeployed)
            {
                System.Deployment.Application.ApplicationDeployment ad = System.Deployment.Application.ApplicationDeployment.CurrentDeployment;
                this.Title += " (v. " + ad.CurrentVersion.ToString() + ")";
            }

            this.varsCache.BindWithTextBox("consumer_key", this.consumerKeyTextbox);
            this.varsCache.BindWithTextBox("consumer_secret", this.consumerSecretTextbox);
            this.varsCache.BindWithTextBox("token", this.tokenTextbox);
            this.varsCache.BindWithTextBox("token_secret", this.tokenSecretTextbox);

            /* We use a "mother installation" for the first USOS API request. We need to
             * get a list of all USOS API installations. */

            var motherInstallation = new ApiInstallation
            {
                base_url = "http://apps.usos.edu.pl/" // will change when out of Beta!
            };
            this.apiConnector = new ApiConnector(motherInstallation);
            this.apiConnector.BeginRequest += new EventHandler(apiConnector_BeginRequest);
            this.apiConnector.EndRequest += new EventHandler(apiConnector_EndRequest);

            /* Fill up the installations list. */

            try
            {
                this.installationsComboBox.Items.Clear();
                foreach (var installation in this.apiConnector.GetInstallations())
                {
                    this.installationsComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = installation.base_url,
                        Tag = installation
                    });
                }
            }
            catch (WebException)
            {
                MessageBox.Show("Error occured when trying to access USOS API mother server. Could not populate USOS API installations list.",
                    "Network error", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }

            if (this.installationsComboBox.Items.Count > 0)
            {
                /* Now we have a list of all installations in a combo box. We choose
                 * one of them. */

                this.installationsComboBox.SelectedIndex = 0;
                this.ReloadInstallation();
            }
        }

        void apiConnector_BeginRequest(object sender, EventArgs e)
        {
            /* Change a cursor to Wait when API request begins... */
            this.Cursor = System.Windows.Input.Cursors.Wait;
        }

        void apiConnector_EndRequest(object sender, EventArgs e)
        {
            /* Change a cursor back to Arrow when API request ends. */
            this.Cursor = System.Windows.Input.Cursors.Arrow;
        }

        /// <summary>
        /// List of valid scopes (as retrieved from a currently selected API installation).
        /// </summary>
        public List<ApiScope> scopes;

        /// <summary>
        /// Refresh the list of valid scopes.
        /// </summary>
        private void RefreshScopes()
        {
            this.scopes = this.apiConnector.GetScopes();
        }

        /// <summary>
        /// Refresh the methods tree.
        /// </summary>
        /// <returns>False if could not connect to the current API installation.</returns>
        private bool RefreshTree()
        {
            /* Retrieving a list of all API methods. */

            List<ApiMethod> methods = null;
            try
            {
                methods = this.apiConnector.GetMethods();
            }
            catch (WebException ex)
            {
                MessageBox.Show("Could not connect to selected installation.\n" + ex.Message + "\n"
                    + ApiConnector.ReadResponse(ex.Response), "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not connect to selected installation.\n" + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            /* Building a tree of modules and methods. This is done by analyzing method names. */

            foreach (ApiMethod method in methods)
            {
                string[] path = method.name.Split('/');
                TreeViewItem currentnode = null;
                for (var i = 1; i < path.Length; i++)
                {
                    string part = path[i];
                    bool already_present = false;
                    ItemCollection items = (i == 1) ? this.methodsTreeView.Items : currentnode.Items;
                    foreach (TreeViewItem item in items)
                    {
                        if ((string)item.Header == part) {
                            already_present = true;
                            currentnode = item;
                        }
                    }
                    if (!already_present)
                    {
                        currentnode = new TreeViewItem { Header = part };
                        if (i == path.Length - 1)
                        {
                            currentnode.Tag = method;
                            currentnode.ToolTip = method.brief_description;
                        }
                        items.Add(currentnode);
                    }
                }
            }

            /* Expand all nodes of the tree (if the are more than 50 methods in this installation,
             * then we skip this step), and select an initial method (request_token). */

            foreach (TreeViewItem item in this.methodsTreeView.Items)
            {
                if (methods.Count < 50)
                    item.ExpandSubtree();
                if ((string)item.Header == "oauth")
                {
                    item.ExpandSubtree();
                    foreach (TreeViewItem subitem in item.Items)
                        if ((string)subitem.Header == "request_token")
                            subitem.IsSelected = true;
                }
            }
            return true;
        }

        private Dictionary<string, TextBox> methodArgumentsTextboxes = new Dictionary<string,TextBox>();
        private CheckBox signWithConsumerSecretCheckbox;
        private CheckBox signWithTokenSecretCheckbox;
        private TextBox methodResultTextbox;
        private CheckBox methodResultHumanReadableCheckbox;
        private CheckBox useSslCheckbox;

        private void methodsTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            /* User clicked a different method in the tree. We will reset and rebuild
             * the right side of the browser window. */

            TreeViewItem selected = (TreeViewItem)this.methodsTreeView.SelectedItem;
            this.methodArgumentsTextboxes.Clear();
            this.mainDockingPanel.Children.Clear();
            if (selected == null)
                return;

            /* The upper panel - for the method call parameters, etc. Docked in the top. */

            var formStackPanel = new StackPanel
            {
                Width = Double.NaN,
                Height = Double.NaN,
                Orientation = Orientation.Vertical,
            };
            var scrollViewer = new ScrollViewer
            {
                Width = Double.NaN,
                Height = this.getScrollViewerHeight(),
                Margin = new Thickness(0, 0, 0, 10),
            };
            scrollViewer.Content = formStackPanel;
            this.mainDockingPanel.Children.Add(scrollViewer);
            DockPanel.SetDock(scrollViewer, Dock.Top);

            if (!(selected.Tag is ApiMethod))
                return;

            var method = this.apiConnector.GetMethod(((ApiMethod)selected.Tag).name);

            /* A header with the method's name. */

            formStackPanel.Children.Add(new Label
            {
                Content = method.brief_description.Replace("_", "__"),
                FontWeight = FontWeights.Bold,
                FontSize = 14
            });

            /* Hyperlink to method's description page. */

            var aBlockWithALink = new TextBlock { Margin = new Thickness(152, 2, 2, 2) };
            var methodDescriptionHyperlink = new Hyperlink { Tag = method.ref_url };
            methodDescriptionHyperlink.Inlines.Add("view full description of this method");
            methodDescriptionHyperlink.Click += new RoutedEventHandler(methodDescriptionLink_Click);
            aBlockWithALink.Inlines.Add(methodDescriptionHyperlink);
            formStackPanel.Children.Add(aBlockWithALink);

            /* Stacking method arguments textboxes... */

            var arguments = method.arguments;
            if (method.auth_options_token != "ignored")
                arguments.Add(new ApiMethodArgument { name = "as_user_id" });
            foreach (var arg in method.arguments)
            {
                var singleArgumentStackPanel = new StackPanel
                {
                    Width = Double.NaN,
                    Height = Double.NaN,
                    Orientation = Orientation.Horizontal
                };

                /* Adding a label with a name of the argument. */

                singleArgumentStackPanel.Children.Add(new Label
                {
                    Width = 150,
                    Height = 28,
                    Content = arg.name.Replace("_", "__") + ":",
                    FontStyle = (arg.is_required ? FontStyles.Normal : FontStyles.Italic),
                    FontWeight = (arg.is_required ? FontWeights.Bold : FontWeights.Normal),
                });

                /* Adding a textbox for a value. */

                var textbox = new TextBox { Width = 280, Height = 23, Tag = arg, BorderBrush = new SolidColorBrush(Colors.Gray) };
                singleArgumentStackPanel.Children.Add(textbox);

                /* Binding textbox value to cache. This will cause the text to be automatically
                 * filled with a value that was previously entered to it. */

                this.varsCache.BindWithTextBox(method.name + "#" + arg.name, textbox);

                /* Saving each textbox instance in a dictionary, in order to easily
                 * access it later. */

                this.methodArgumentsTextboxes.Add(arg.name, textbox);

                /* Stacking the entire thing on the form stack panel. */

                formStackPanel.Children.Add(singleArgumentStackPanel);
            }

            /* Just a small margin. */

            formStackPanel.Children.Add(new Label { Height = 8 });

            /* SSL checkbox. */

            this.useSslCheckbox = new CheckBox { Content = "Use SSL" + (method.auth_options_ssl_required == true ? " (required)" : ""),
                Margin = new Thickness(150, 0, 0, 0) };
            formStackPanel.Children.Add(this.useSslCheckbox);
            this.varsCache.BindWithCheckBox("use_ssl", this.useSslCheckbox);

            /* "Sign with..." checkboxes. */

            this.signWithConsumerSecretCheckbox = new CheckBox { Content = "Sign with Consumer Key (" + method.auth_options_consumer + ")",
                Margin = new Thickness(150, 0, 0, 0) };
            this.signWithConsumerSecretCheckbox.Checked += new RoutedEventHandler(consumersigncheckbox_Checked);
            this.signWithConsumerSecretCheckbox.Unchecked += new RoutedEventHandler(consumersigncheckbox_Unchecked);
            formStackPanel.Children.Add(this.signWithConsumerSecretCheckbox);
            this.signWithTokenSecretCheckbox = new CheckBox { Content = "Sign with Token (" + method.auth_options_token + ")",
                IsEnabled = false, Margin = new Thickness(150, 0, 0, 0) };
            formStackPanel.Children.Add(this.signWithTokenSecretCheckbox);
            this.varsCache.BindWithCheckBox("sign_with_consumer_key", this.signWithConsumerSecretCheckbox);
            this.varsCache.BindWithCheckBox("sign_with_token", this.signWithTokenSecretCheckbox);

            /* Execute/Launch buttons. */

            var buttonsStack = new StackPanel
            {
                Width = Double.NaN,
                Height = Double.NaN,
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(150, 0, 0, 0)
            };
            var theExecuteButton = new Button
            {
                Content = "Execute!",
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 8, 0, 8)
            };
            theExecuteButton.Click += new RoutedEventHandler(executeButton_click);
            buttonsStack.Children.Add(theExecuteButton);
            var launchInBrowserButton = new Button
            {
                Content = "Launch in Browser",
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(8, 8, 0, 8)
            };
            launchInBrowserButton.Click += new RoutedEventHandler(browserButton_click);
            buttonsStack.Children.Add(launchInBrowserButton);
            formStackPanel.Children.Add(buttonsStack);

            /* "Try to make it readable" checkbox. */

            this.methodResultHumanReadableCheckbox = new CheckBox { Content = "Try to make it more human-readable" };
            this.methodResultHumanReadableCheckbox.Click += new RoutedEventHandler(executeButton_click);
            this.varsCache.BindWithCheckBox("make_it_readable", this.methodResultHumanReadableCheckbox);
            formStackPanel.Children.Add(this.methodResultHumanReadableCheckbox);

            /* We fill all the rest of the main docking panel with a single textbox - the results. */

            this.methodResultTextbox = new TextBox
            {
                BorderBrush = new SolidColorBrush(Colors.Gray),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Courier New")
            };
            this.mainDockingPanel.Children.Add(this.methodResultTextbox);
        }

        private double getScrollViewerHeight()
        {
            var parentHeight = this.mainDockingPanel.ActualHeight;
            return parentHeight / 2;
        }

        void consumersigncheckbox_Checked(object sender, RoutedEventArgs e)
        {
            this.signWithTokenSecretCheckbox.IsEnabled = true;
        }

        void consumersigncheckbox_Unchecked(object sender, RoutedEventArgs e)
        {
            /* When a user unchecks the "sign with consumer key" checkbox, we make
             * the other one (sign with a token) disabled (you can't sign with only
             * a token). */

            this.signWithTokenSecretCheckbox.IsChecked = false;
            this.signWithTokenSecretCheckbox.IsEnabled = false;
        }

        void methodDescriptionLink_Click(object sender, RoutedEventArgs e)
        {
            /* User clicked a hyperlink to a method's description. */

            Hyperlink link = (Hyperlink)sender;
            string url = (string)link.Tag;
            try
            {
                System.Diagnostics.Process.Start(url);
            }
            catch (Exception)
            {
                MessageBox.Show("It appears your system doesn't know in which application to open a http:// protocol. Check your browser settings.\n\n" + url);
                return;
            }
        }
        
        /// <summary>
        /// Get a dictionary of currently filled argument values of 
        /// a currently displayed method.
        /// </summary>
        Dictionary<string, string> GetMethodArgs()
        {
            Dictionary<string, string> results = new Dictionary<string, string>();
            foreach (var pair in this.methodArgumentsTextboxes)
            {
                string key = pair.Key;
                string value = pair.Value.Text;
                if (value.Length > 0)
                    results.Add(key, value);
            }
            return results;
        }

        /// <summary>
        /// Get an URL of a currently displayed method, with all the arguments
        /// and signatures applied - according to all the textboxes and checkboxes
        /// in a form.
        /// </summary>
        string GetMethodURL()
        {
            TreeViewItem selected = (TreeViewItem)this.methodsTreeView.SelectedItem;
            ApiMethod method = (ApiMethod)selected.Tag;
            Dictionary<string, string> args = this.GetMethodArgs();
            string url = this.apiConnector.GetURL(method, args,
                this.signWithConsumerSecretCheckbox.IsChecked == true ? this.consumerKeyTextbox.Text : "",
                this.signWithConsumerSecretCheckbox.IsChecked == true ? this.consumerSecretTextbox.Text : "",
                this.signWithTokenSecretCheckbox.IsChecked == true ? this.tokenTextbox.Text : "",
                this.signWithTokenSecretCheckbox.IsChecked == true ? this.tokenSecretTextbox.Text : "",
                this.useSslCheckbox.IsChecked == true ? true : false);
            return url;
        }

        /// <summary>
        /// User clicks the Execute button.
        /// </summary>
        void executeButton_click(object sender, RoutedEventArgs e)
        {
            try
            {
                string probably_json = this.apiConnector.GetResponse(this.GetMethodURL());
                if (this.methodResultHumanReadableCheckbox.IsChecked == true)
                {
                    try
                    {
                        object obj = JsonConvert.DeserializeObject(probably_json);
                        if (obj == null)
                            this.methodResultTextbox.Text = probably_json;
                        else
                            this.methodResultTextbox.Text = obj.ToString().Replace("\\t", "    ").Replace("\\n", "\n");
                    }
                    catch (JsonReaderException)
                    {
                        this.methodResultTextbox.Text = probably_json;
                    }
                }
                else
                {
                    this.methodResultTextbox.Text = probably_json;
                }
            }
            catch (WebException ex)
            {
                this.methodResultTextbox.Text = ex.Message + "\n" + ApiConnector.ReadResponse(ex.Response);
            }
        }

        /// <summary>
        /// User clicks the "Launch in Browser" button.
        /// </summary>
        void browserButton_click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(this.GetMethodURL());
            }
            catch (Exception)
            {
                MessageBox.Show("It appears your system doesn't know in which application to open a http:// protocol. Check your browser settings.\n\n" + this.GetMethodURL());
                return;
            }
        }

        /// <summary>
        /// User clicks the "Quick Fill" button.
        /// </summary>
        private void quickFillButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.consumerKeyTextbox.Text == "")
            {
                if (MessageBox.Show("In order to get Access Tokens, you have to register a Consumer Key first. " +
                    "Would you like to register a new Consumer Key now?", "Consumer Key is missing", MessageBoxButton.OKCancel,
                    MessageBoxImage.Question) == MessageBoxResult.OK)
                {
                    /* Direct the user to USOS API Developer Center. */
                    try
                    {
                        System.Diagnostics.Process.Start(this.apiConnector.GetURL(new ApiMethod { name = "developers/" }));
                    }
                    catch (Exception)
                    {
                        MessageBox.Show("It appears your system doesn't know in which application to open a http:// protocol. Check your browser settings.\n\n" + this.apiConnector.GetURL(new ApiMethod { name = "developers/" }));
                        return;
                    }
                }
                return;
            }

            /* Show initial dialog, user will choose desired scopes. */

            var initialDialog = new QuickFillWindow();
            initialDialog.Owner = this;
            if (initialDialog.ShowDialog() == false)
                return; // user cancelled

            /* Retrieve a list of selected scopes. */

            List<string> scopeKeys = initialDialog.GetSelectedScopeKeys();

            /* Build request_token URL. We will use 'oob' as callback, and
             * require scopes which the user have selected. */

            var request_token_args = new Dictionary<string,string>();
            request_token_args.Add("oauth_callback", "oob");
            if (scopeKeys.Count > 0)
                request_token_args.Add("scopes", string.Join("|", scopeKeys));
            string request_token_url = this.apiConnector.GetURL(new ApiMethod { name = "services/oauth/request_token" },
                request_token_args, this.consumerKeyTextbox.Text, this.consumerSecretTextbox.Text, "", "", true);

            try
            {
                /* Get and parse the request_token response string. */

                string tokenstring = this.apiConnector.GetResponse(request_token_url);
                string request_token = null;
                string request_token_secret = null;
                string[] parts = tokenstring.Split('&');
                foreach (string part in parts)
                {
                    if (part.StartsWith("oauth_token="))
                        request_token = part.Substring("oauth_token=".Length);
                    if (part.StartsWith("oauth_token_secret="))
                        request_token_secret = part.Substring("oauth_token_secret=".Length);
                }
                if (request_token == null || request_token_secret == null)
                {
                    MessageBox.Show("Couldn't parse request token. Try to do this sequence manually!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                /* Build authorize URL and open it in user's browser. */

                var authorize_args = new Dictionary<string, string>();
                authorize_args.Add("oauth_token", request_token);
                var authorize_url = this.apiConnector.GetURL(new ApiMethod { name = "services/oauth/authorize" }, authorize_args);
                try
                {
                    System.Diagnostics.Process.Start(authorize_url);
                }
                catch (Exception)
                {
                    MessageBox.Show("It appears your system doesn't know in which application to open a http:// protocol. Check your browser settings.\n\n" + authorize_url);
                    return;
                }
                
                /* Open a with PIN request and wait for user's entry. */

                var pinWindow = new QuickFillPINWindow();
                pinWindow.Owner = this;
                pinWindow.ShowDialog();
                string verifier = pinWindow.GetPIN();

                /* Build the access_token URL. */

                var access_token_args = new Dictionary<string, string>();
                access_token_args.Add("oauth_verifier", verifier);
                var access_token_url = this.apiConnector.GetURL(new ApiMethod { name = "services/oauth/access_token" }, access_token_args,
                    this.consumerKeyTextbox.Text, this.consumerSecretTextbox.Text, request_token, request_token_secret, true);

                /* Get and parse the access_token response string. */

                tokenstring = this.apiConnector.GetResponse(access_token_url);
                string access_token = null;
                string access_token_secret = null;
                parts = tokenstring.Split('&');
                foreach (string part in parts)
                {
                    if (part.StartsWith("oauth_token="))
                        access_token = part.Substring("oauth_token=".Length);
                    if (part.StartsWith("oauth_token_secret="))
                        access_token_secret = part.Substring("oauth_token_secret=".Length);
                }
                if (access_token == null || access_token_secret == null)
                {
                    MessageBox.Show("Couldn't parse access token. Try to do this sequence manually!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                /* Fill up the token textboxes with an Access Token we received. */

                this.tokenTextbox.Text = access_token;
                this.tokenSecretTextbox.Text = access_token_secret;
            }
            catch (WebException ex)
            {
                MessageBox.Show("A problem occured. Couldn't complete the Quick Fill.\n\n" + ex.Message + "\n"
                    + ApiConnector.ReadResponse(ex.Response), "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
                
        }

        private void ReloadInstallation()
        {
            this.methodsTreeView.Items.Clear();
            this.quickFillButton.IsEnabled = false;
            
            /* Checking which installation is selected in a combo box. */

            if (this.apiConnector.currentInstallation.base_url != this.installationsComboBox.Text)
            {
                this.apiConnector.SwitchInstallation(new ApiInstallation { base_url = this.installationsComboBox.Text });
            }

            if (!this.RefreshTree())
                return;
            this.RefreshScopes();

            /* We did retrieve the list of methods, so the installation URL is OK. If it was
             * entered manually (was not on the installation list in a combo box), then we add
             * it to the list. */

            var onthelist = false;
            foreach (object item in this.installationsComboBox.Items)
            {
                ApiInstallation itemapi = (ApiInstallation)((ComboBoxItem)item).Tag;
                if (itemapi.base_url == this.apiConnector.currentInstallation.base_url)
                    onthelist = true;
            }
            if (!onthelist)
            {
                this.installationsComboBox.Items.Add(new ComboBoxItem
                {
                    Content = this.apiConnector.currentInstallation.base_url,
                    Tag = this.apiConnector.currentInstallation
                });
            }
            this.quickFillButton.IsEnabled = true;

        }

        private void installationRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            this.ReloadInstallation();
        }

        private void installationsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.dropdownopen)
            {
                //this.ReloadInstallation();
            }
        }

        private bool dropdownopen = false;
        private CheckBox runAsUserCheckbox;
        private void installationsComboBox_DropDownClosed(object sender, EventArgs e)
        {
            this.dropdownopen = false;
        }

        private void installationsComboBox_DropDownOpened(object sender, EventArgs e)
        {
            this.dropdownopen = true;
        }

        private void installationsComboBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                this.ReloadInstallation();
            }
        }
    }
}
