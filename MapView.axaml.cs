using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Svg.Skia;

namespace map_app;

public partial class MapView : Window
{
    public GraphDataDto LoadedGraph { get; private set; } = new();

    private NodeDto? _selectedEdgeSource = null;
    private NodeDto? _editingNode = null;
    private EdgeDto? _editingEdge = null;
    private Point? _pendingNodePos = null;
    private bool _areLinksHighlighted;
    private int _nextId = 1;

    public MapView()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        string imageUri = "avares://map_app/Assets/map.svg";
        string jsonUri = "avares://map_app/Assets/graph.json";

        LoadMapImage(imageUri);
        LoadGraphData(jsonUri);
    }

    private const double MapWidth = 1280;
    private const double MapHeight = 853;
    private const double WalkingSpeed = 1.4;

    private void LoadMapImage(string resourceUri)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(resourceUri));
            var svg = SvgSource.LoadFromStream(stream);
            MapImage.Source = new SvgImage { Source = svg };

            MapImage.Width = MapWidth;
            MapImage.Height = MapHeight;

            GraphCanvas.Width = MapWidth;
            GraphCanvas.Height = MapHeight;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Ошибка загрузки карты: {ex.Message}";
        }
    }

    private void LoadGraphData(string resourceUri)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(resourceUri));
            using var reader = new StreamReader(stream);
            string jsonContent = reader.ReadToEnd();
            var data = JsonSerializer.Deserialize<GraphDataDto>(jsonContent);

            if (data != null)
            {
                LoadedGraph = data;
                if (LoadedGraph.Nodes.Count > 0)
                {
                    _nextId = LoadedGraph.Nodes.Max(n => n.Id) + 1;
                }
            }
        }
        catch (Exception)
        {
            StatusTextBlock.Text = "Не удалось загрузить исходный JSON.";
        }

        RedrawGraph();
    }

    private void RedrawGraph()
    {
        GraphCanvas.Children.Clear();

        var nodeMap = LoadedGraph.Nodes.ToDictionary(n => n.Id);

        foreach (var edge in LoadedGraph.Edges)
        {
            if (nodeMap.TryGetValue(edge.Source, out var source) && nodeMap.TryGetValue(edge.Target, out var target))
            {
                var start = new Point(source.X, source.Y);
                var end = new Point(target.X, target.Y);

                var line = new Line
                {
                    StartPoint = start,
                    EndPoint = end,
                    Stroke = _areLinksHighlighted ? Brushes.Gold : Brushes.Cyan,
                    StrokeThickness = _areLinksHighlighted ? 6 : 3
                };
                GraphCanvas.Children.Add(line);

                var hitArea = new Line
                {
                    StartPoint = start,
                    EndPoint = end,
                    Stroke = Brushes.Transparent,
                    StrokeThickness = 12,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = edge
                };

                ToolTip.SetTip(hitArea, $"Связь {edge.Source} -> {edge.Target} | Время: {edge.Time.ToString(CultureInfo.InvariantCulture)} с");
                hitArea.PointerPressed += OnEdgePointerPressed;

                GraphCanvas.Children.Add(hitArea);
            }
        }

        double radius = 8.0;

        foreach (var node in LoadedGraph.Nodes)
        {
            IBrush fillBrush = node == _selectedEdgeSource ? Brushes.Yellow : GetNodeColor(node.Type);

            var circle = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = fillBrush,
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                Tag = node
            };

            ToolTip.SetTip(circle, GetNodeTooltip(node));

            Canvas.SetLeft(circle, node.X - radius);
            Canvas.SetTop(circle, node.Y - radius);

            circle.PointerPressed += OnNodePointerPressed;

            GraphCanvas.Children.Add(circle);
        }

        StatusTextBlock.Text = $"Узлов: {LoadedGraph.Nodes.Count} | Связей: {LoadedGraph.Edges.Count}" +
            (_selectedEdgeSource != null ? $" | Выбран узел ID {_selectedEdgeSource.Id}" : "");
    }

    private IBrush GetNodeColor(string type) => type switch
    {
        "waypoint" => Brushes.DodgerBlue,
        "entrance" => Brushes.LimeGreen,
        "stairs" => Brushes.Orange,
        "ramp" => Brushes.MediumPurple,
        _ => Brushes.Red
    };

    private string GetNodeTooltip(NodeDto node) => node.Type switch
    {
        "waypoint" => $"ID: {node.Id} [Точка: {node.Name}]",
        "entrance" => $"ID: {node.Id} [Вход] Доступ: {node.Accessible}, Здание: #{node.Building}",
        "stairs" => $"ID: {node.Id} [Лестница]",
        "ramp" => $"ID: {node.Id} [Пандус]",
        _ => $"ID: {node.Id} [{node.Type}]"
    };

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointProps = e.GetCurrentPoint(GraphCanvas).Properties;

        if (pointProps.IsLeftButtonPressed)
        {
            _pendingNodePos = e.GetPosition(GraphCanvas);
            _editingNode = null;

            OverlayTitle.Text = "Новая точка";
            NodeTypeComboBox.SelectedIndex = 0;
            NodeNameInput.Text = string.Empty;
            NodeAccessibleCheckBox.IsChecked = false;
            DeleteNodeButton.IsVisible = false;

            UpdateBuildingComboBox();
            UpdateFieldsVisibility("road");

            EditNodeOverlay.IsVisible = true;
        }
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(GraphCanvas);
        CursorPositionTextBlock.Text = $"Курсор: X {position.X.ToString("0.0", CultureInfo.InvariantCulture)}, Y {position.Y.ToString("0.0", CultureInfo.InvariantCulture)}";
    }

    private void OnCanvasPointerExited(object? sender, PointerEventArgs e)
    {
        CursorPositionTextBlock.Text = "Курсор: X —, Y —";
    }

    private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;

        if (sender is Ellipse ellipse && ellipse.Tag is NodeDto node)
        {
            var pointProps = e.GetCurrentPoint(GraphCanvas).Properties;

            if (pointProps.IsLeftButtonPressed)
            {
                _editingNode = node;
                _pendingNodePos = null;

                OverlayTitle.Text = $"Редактирование ID: {node.Id}";
                NodeNameInput.Text = node.Name ?? string.Empty;
                NodeAccessibleCheckBox.IsChecked = node.Accessible ?? false;
                DeleteNodeButton.IsVisible = true;

                UpdateBuildingComboBox();
                SelectBuildingInComboBox(node.Building);

                for (int i = 0; i < NodeTypeComboBox.Items.Count; i++)
                {
                    if (NodeTypeComboBox.Items[i] is ComboBoxItem item && item.Tag?.ToString() == node.Type)
                    {
                        NodeTypeComboBox.SelectedIndex = i;
                        break;
                    }
                }

                UpdateFieldsVisibility(node.Type);
                EditNodeOverlay.IsVisible = true;
            }
            else if (pointProps.IsRightButtonPressed)
            {
                if (_selectedEdgeSource == null)
                {
                    _selectedEdgeSource = node;
                }
                else if (_selectedEdgeSource == node)
                {
                    _selectedEdgeSource = null;
                }
                else
                {
                    bool exists = LoadedGraph.Edges.Any(edge => 
                        (edge.Source == _selectedEdgeSource.Id && edge.Target == node.Id) ||
                        (edge.Source == node.Id && edge.Target == _selectedEdgeSource.Id));

                    if (!exists)
                    {
                        LoadedGraph.Edges.Add(new EdgeDto
                        {
                            Source = _selectedEdgeSource.Id,
                            Target = node.Id,
                            Time = CalculateTravelSeconds(_selectedEdgeSource, node)
                        });
                    }

                    _selectedEdgeSource = null;
                }

                RedrawGraph();
            }
        }
    }

    private void OnEdgePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;

        if (sender is not Line line || line.Tag is not EdgeDto edge) return;

        var pointProps = e.GetCurrentPoint(GraphCanvas).Properties;

        if (pointProps.IsRightButtonPressed)
        {
            LoadedGraph.Edges.Remove(edge);
            RedrawGraph();
        }
        else if (pointProps.IsLeftButtonPressed)
        {
            _editingEdge = edge;

            EdgeOverlayTitle.Text = $"Связь {edge.Source} -> {edge.Target}";
            EdgeTimeInput.Text = edge.Time.ToString(CultureInfo.InvariantCulture);
            EdgeTimeError.IsVisible = false;

            EditEdgeOverlay.IsVisible = true;
        }
    }

    private void OnSaveEdgeEdit(object? sender, RoutedEventArgs e)
    {
        if (_editingEdge == null)
        {
            EditEdgeOverlay.IsVisible = false;
            return;
        }

        string raw = (EdgeTimeInput.Text ?? string.Empty).Trim().Replace(',', '.');

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || seconds < 0)
        {
            EdgeTimeError.IsVisible = true;
            return;
        }

        _editingEdge.Time = seconds;

        EditEdgeOverlay.IsVisible = false;
        _editingEdge = null;
        RedrawGraph();
    }

    private void OnDeleteEdgeClick(object? sender, RoutedEventArgs e)
    {
        if (_editingEdge != null)
        {
            LoadedGraph.Edges.Remove(_editingEdge);
        }

        EditEdgeOverlay.IsVisible = false;
        _editingEdge = null;
        RedrawGraph();
    }

    private void OnCancelEdgeEdit(object? sender, RoutedEventArgs e)
    {
        EditEdgeOverlay.IsVisible = false;
        _editingEdge = null;
    }

    private void OnNodeTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (NodeTypeComboBox?.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            UpdateFieldsVisibility(item.Tag.ToString()!);
        }
    }

    private void UpdateFieldsVisibility(string type)
    {
        if (NamePanel == null || EntrancePanel == null) return;

        NamePanel.IsVisible = type == "waypoint";
        EntrancePanel.IsVisible = type == "entrance";
    }

    private void UpdateBuildingComboBox()
    {
        if (BuildingWaypointsComboBox == null) return;

        BuildingWaypointsComboBox.Items.Clear();

        var waypoints = LoadedGraph.Nodes.Where(n => n.Type == "waypoint").ToList();

        foreach (var wp in waypoints)
        {
            BuildingWaypointsComboBox.Items.Add(new ComboBoxItem
            {
                Content = $"#{wp.Id} - {wp.Name}",
                Tag = wp.Id
            });
        }
    }

    private void SelectBuildingInComboBox(int? buildingId)
    {
        if (BuildingWaypointsComboBox == null) return;

        if (!buildingId.HasValue)
        {
            BuildingWaypointsComboBox.SelectedIndex = -1;
            return;
        }

        for (int i = 0; i < BuildingWaypointsComboBox.Items.Count; i++)
        {
            if (BuildingWaypointsComboBox.Items[i] is ComboBoxItem item && item.Tag is int id && id == buildingId.Value)
            {
                BuildingWaypointsComboBox.SelectedIndex = i;
                break;
            }
        }
    }

    private void OnSaveNodeEdit(object? sender, RoutedEventArgs e)
    {
        string type = (NodeTypeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "road";
        string? name = type == "waypoint" ? NodeNameInput.Text?.Trim() : null;
        bool? accessible = type == "entrance" ? NodeAccessibleCheckBox.IsChecked : null;

        int? building = null;
        if (type == "entrance" && BuildingWaypointsComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is int bId)
        {
            building = bId;
        }

        if (_editingNode != null)
        {
            _editingNode.Type = type;
            _editingNode.Name = name;
            _editingNode.Accessible = accessible;
            _editingNode.Building = building;
        }
        else if (_pendingNodePos.HasValue)
        {
            var newNode = new NodeDto
            {
                Id = _nextId++,
                Type = type,
                Name = name,
                Accessible = accessible,
                Building = building,
                X = Math.Round(_pendingNodePos.Value.X, 1),
                Y = Math.Round(_pendingNodePos.Value.Y, 1)
            };

            LoadedGraph.Nodes.Add(newNode);
        }

        EditNodeOverlay.IsVisible = false;
        RedrawGraph();
    }

    private void OnDeleteNodeClick(object? sender, RoutedEventArgs e)
    {
        if (_editingNode != null)
        {
            int deletedId = _editingNode.Id;

            LoadedGraph.Nodes.Remove(_editingNode);
            LoadedGraph.Edges.RemoveAll(edge => edge.Source == deletedId || edge.Target == deletedId);

            if (_selectedEdgeSource?.Id == deletedId)
            {
                _selectedEdgeSource = null;
            }
        }

        EditNodeOverlay.IsVisible = false;
        _editingNode = null;
        _pendingNodePos = null;

        RedrawGraph();
    }

    private void OnCancelNodeEdit(object? sender, RoutedEventArgs e)
    {
        EditNodeOverlay.IsVisible = false;
        _editingNode = null;
        _pendingNodePos = null;
    }

    private async void OnSaveJsonClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить граф в JSON",
            DefaultExtension = "json",
            SuggestedFileName = "graph.json",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSON файлы") { Patterns = new[] { "*.json" } }
            }
        });

        if (file != null)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            string json = JsonSerializer.Serialize(LoadedGraph, options);
            await using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);

            StatusTextBlock.Text = "Граф успешно сохранен в файл!";
        }
    }

    private void OnClearGraphClick(object? sender, RoutedEventArgs e)
    {
        LoadedGraph.Nodes.Clear();
        LoadedGraph.Edges.Clear();
        _selectedEdgeSource = null;
        _editingEdge = null;
        _nextId = 1;
        RedrawGraph();
    }

    private void OnResetZoomClick(object? sender, RoutedEventArgs e)
    {
        ZoomControl?.ResetMatrix();
    }

    private static double CalculateTravelSeconds(NodeDto from, NodeDto to)
    {
        double deltaX = from.X - to.X;
        double deltaY = from.Y - to.Y;
        double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        return Math.Max(1, Math.Round(distance / WalkingSpeed));
    }

    private void OnHighlightLinksClick(object? sender, RoutedEventArgs e)
    {
        _areLinksHighlighted = !_areLinksHighlighted;
        HighlightLinksButton.Content = _areLinksHighlighted ? "Убрать подсветку" : "Подсветить связи";
        RedrawGraph();
    }
}
