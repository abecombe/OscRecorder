using System;
using RosettaUI;
using UnityEngine;

public static class CustomUI
{
    public static SpaceElement BlankLine(float height = 10f) => UI.Space().SetHeight(height);

    public static LabelElement BoldLabel(string label) => UI.Label("<b>" + label + "</b>");

    public static ButtonElement SetButtonColor(this ButtonElement element, Color? color)
    {
        element.Style.Color = Color.white;
        element.Style.BackgroundColor = color;
        return element;
    }

    public static Element HorizontalLine(float upperSpace = 10f, float lowerSpace = 10f)
    {
        return UI.Column(
            BlankLine(upperSpace),
            UI.Box().SetHeight(5f).SetBackgroundColor(Color.gray),
            BlankLine(lowerSpace)
        );
    }

    public static Element SliderWithoutInputField<T>(LabelElement label, Func<T> readValue, Action<T> writeValue, T min, T max)
    {
        return UI.Slider(label, Binder.Create(readValue, writeValue),
            new SliderOption
            {
                minGetter = ConstGetter.Create(min),
                maxGetter = ConstGetter.Create(max),
                showInputField = false
            });
    }
}