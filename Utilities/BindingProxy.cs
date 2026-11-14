// ═══════════════════════════════════════════════════════════════════════
// FILE: Utilities/BindingProxy.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — XAML Binding Infrastructure
// VERSION: Production Station Grid — 2026-09-18
//
// PURPOSE:
//   Lets objects that are NOT in the visual/logical tree bind to the
//   ViewModel. `DataGridColumn` is the classic case: columns live in
//   DataGrid.Columns, which is not part of either tree, so a binding written
//   directly on a column property (`Header="{Binding InColumnHeader}"`,
//   `Visibility="{Binding ShowSizeColumn}"`) resolves against nothing and
//   silently renders empty. That is exactly what the production station grid
//   did: the "TOTAL / DONE / BALANCE" column's per-station header never
//   appeared, and six columns the stage ViewModel explicitly switches off
//   (`ShowLmColumn`, `ShowLoadColumn`, …) stayed visible, starving the
//   columns that matter of width.
//
//   A `Freezable` is used because Freezables participate in WPF's
//   *inheritance context*: when the proxy is declared in an element's
//   Resources, `Data="{Binding}"` picks up that element's DataContext without
//   any RelativeSource walk (which would not resolve from inside a resource
//   dictionary). Columns then bind with an explicit
//   `Source={StaticResource proxy}`, which needs no tree at all.
//
// USAGE:
//   <DataGrid.Resources>
//       <util:BindingProxy x:Key="Vm" Data="{Binding}"/>
//   </DataGrid.Resources>
//   ...
//   <DataGridTextColumn Header="LM"
//       Visibility="{Binding Data.ShowLmColumn, Source={StaticResource Vm},
//                     Converter={StaticResource BoolToVis}}"/>
// ═══════════════════════════════════════════════════════════════════════

using System.Windows;

namespace ProGlassAutomation.Utilities;

/// <summary>
/// Carries a DataContext to bindings that cannot walk the visual tree
/// (DataGridColumn, Style setters on non-tree objects, resource-dictionary
/// entries). Declare it in an element's Resources and bind
/// <c>Data="{Binding}"</c>.
/// </summary>
public sealed class BindingProxy : Freezable
{
    /// <summary>
    /// The payload — normally the ViewModel of the element whose Resources
    /// hold this proxy.
    /// </summary>
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(
            nameof(Data),
            typeof(object),
            typeof(BindingProxy),
            new PropertyMetadata(null));

    /// <inheritdoc cref="DataProperty"/>
    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>Required Freezable plumbing — creates the cloning instance.</summary>
    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
