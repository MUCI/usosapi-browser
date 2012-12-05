using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

public class VarsCache
{
    private Dictionary<string, string> cache = new Dictionary<string, string>();
    private Dictionary<TextBox, string> textbox_binding = new Dictionary<TextBox, string>();
    private Dictionary<CheckBox, string> checkbox_binding = new Dictionary<CheckBox, string>();

    public string GetVar(string varName, string default_value)
    {
        if (cache.ContainsKey(varName))
            return cache[varName];
        return default_value;
    }
    public void SetVar(string varName, string value)
    {
        cache[varName] = value;
    }
    public void BindWithTextBox(string varName, TextBox textbox)
    {
        textbox.Text = this.GetVar(varName, textbox.Text);
        textbox.TextChanged += new TextChangedEventHandler(textbox_TextChanged);
        this.textbox_binding.Add(textbox, varName);
    }

    void textbox_TextChanged(object sender, TextChangedEventArgs e)
    {
        TextBox textbox = (TextBox)e.Source;
        string varName = this.textbox_binding[textbox];
        SetVar(varName, textbox.Text);
    }

    public void BindWithCheckBox(string varName, CheckBox checkbox)
    {
        checkbox.IsChecked = this.GetVar(varName, checkbox.IsChecked == true ? "true" : "false") == "true";
        //checkbox.Click += new RoutedEventHandler(checkbox_Click);
        checkbox.Checked += new RoutedEventHandler(checkbox_Click);
        checkbox.Unchecked += new RoutedEventHandler(checkbox_Click);
        this.checkbox_binding.Add(checkbox, varName);
    }

    void checkbox_Click(object sender, RoutedEventArgs e)
    {
        CheckBox checkbox = (CheckBox)e.Source;
        string varName = this.checkbox_binding[checkbox];
        SetVar(varName, checkbox.IsChecked == true ? "true" : "false");
    }

}