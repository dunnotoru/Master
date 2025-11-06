using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Master.FormFields;

public partial class FormField : ObservableObject
{
    public string LabelText { get; }

    [ObservableProperty]
    private string? _value;

    protected FormField(string labelText)
    {
        LabelText = labelText;
    }

    public virtual bool Validate([NotNullWhen(false)] out string? message)
    {
        message = null;
        return true;
    }
}