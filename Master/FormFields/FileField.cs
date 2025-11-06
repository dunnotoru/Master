using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Master.FormFields;

public partial class FileField(string labelText) : FormField(labelText)
{

    [RelayCommand]
    private void SelectFile()
    {
        OpenFileDialog dlg = new OpenFileDialog();
        if (dlg.ShowDialog() == true)
        {
            Value = dlg.FileName;
        }
    }

    public override bool Validate([NotNullWhen(false)] out string? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(Value))
        {
            message = $"{LabelText} was null or empty, no file selected";
            return false;
        }

        return true;
    }
}