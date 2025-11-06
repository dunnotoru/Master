using System.Diagnostics.CodeAnalysis;

namespace Master.FormFields;

public class TextField(string labelText) : FormField(labelText)
{
    public override bool Validate([NotNullWhen(false)] out string? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(Value))
        {
            message = $"{LabelText} Value cant be null or WhiteSpace";
            return false;
        }

        return true;
    }
}