using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CardPlayer.Config;

/// <summary>
/// One saved card generator profile. Stores all output settings including the card mode.
/// Inherits ObservableObject so the ComboBox item template binding to Name updates live.
/// </summary>
public partial class PrintGeneratorProfile : ObservableObject
{
    [ObservableProperty] private string _name = "New Profile";
    public string OutputPath           { get; set; } = "";
    public string CardMode             { get; set; } = "Single Card";
    public int    CardWidthPx          { get; set; } = 750;
    public int    CardHeightPx         { get; set; } = 1050;
    public int    QrAboveBottom        { get; set; } = 38;
    public int    HorizontalSpacing    { get; set; } = 0;
    public int    VerticalSpacing      { get; set; } = 0;
    public bool   GenerateFronts       { get; set; } = false;
    public bool   OnlyWithArt          { get; set; } = false;
    public int    ArtBleed             { get; set; } = 0;
    public int    SheetWidthPx         { get; set; } = 2550;
    public int    SheetHeightPx        { get; set; } = 3300;
    public int    VerticalOffset       { get; set; } = 0;
    public bool   DrawOutline          { get; set; } = false;
    public int    OutlineCornerRadius  { get; set; } = 0;
    public bool   FlipFrontsRow        { get; set; } = true;
    public bool   QrGenerateSheet      { get; set; } = false;
    public int    QrSheetWidthPx       { get; set; } = 2550;
    public int    QrSheetHeightPx      { get; set; } = 3300;
    public int    QrAcross             { get; set; } = 4;
    public int    QrDown               { get; set; } = 6;
    public int    QrHMarginCenter      { get; set; } = 150;
    public int    QrVMarginCenter      { get; set; } = 150;
    public int    QrVerticalOffset     { get; set; } = 0;
    public int    QrStartIndex         { get; set; } = 1;
}

/// <summary>Root object saved to Print_Profiles.json</summary>
public class PrintGeneratorProfilesConfig
{
    public string                    ActiveProfile { get; set; } = "";
    public List<PrintGeneratorProfile> Profiles     { get; set; } = new();
}
