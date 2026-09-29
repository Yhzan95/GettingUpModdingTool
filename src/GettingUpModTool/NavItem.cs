using System.Windows;

namespace GettingUpModTool;
public static class NavItem
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(NavItem), new PropertyMetadata(string.Empty));

    public static string GetIcon(DependencyObject element) => (string)element.GetValue(IconProperty);
    public static void SetIcon(DependencyObject element, string value) => element.SetValue(IconProperty, value);

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive", typeof(bool), typeof(NavItem), new PropertyMetadata(false));

    public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);
    public static void SetIsActive(DependencyObject element, bool value) => element.SetValue(IsActiveProperty, value);
}
