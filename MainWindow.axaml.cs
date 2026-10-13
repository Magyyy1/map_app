using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Svg.Skia;
using QuikGraph;
using QuikGraph.Algorithms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace map_app;

public partial class MainWindow : Window
{
    private GraphDataDto? _graph;
    private List<NodeDto> _waypoints = new();
    private const double MapWidth = 1280;
    private const double MapHeight = 853;

    public MainWindow()
    {
        InitializeComponent();
        ComboStart.SelectionChanged += OnRoutePointSelectionChanged;
        ComboEnd.SelectionChanged += OnRoutePointSelectionChanged;
        ChkAccessibleRoute.IsCheckedChanged += OnAccessibleRouteChanged;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        LoadMapImage("avares://map_app/Assets/map.svg");
        LoadGraphData("avares://map_app/Assets/graph.json");
    }

    private void LoadMapImage(string resourceUri)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(resourceUri));
            var svg = SvgSource.LoadFromStream(stream);
            MapImage.Source = new SvgImage { Source = svg };

            MapImage.Width = MapWidth;
            MapImage.Height = MapHeight;

            MapCanvas.Width = MapWidth;
            MapCanvas.Height = MapHeight;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка загрузки карты: {ex.Message}");
        }
    }

    private void LoadGraphData(string resourceUri)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(resourceUri));
            using var reader = new StreamReader(stream);
            string jsonContent = reader.ReadToEnd();

            _graph = JsonSerializer.Deserialize<GraphDataDto>(jsonContent);

            if (_graph != null)
            {
                _waypoints = _graph.Nodes
                    .Where(n => n.Type == "waypoint")
                    .Select(n => new NodeDto
                    {
                        Id = n.Id,
                        Type = n.Type,
                        X = n.X,
                        Y = n.Y,
                        Name = string.IsNullOrWhiteSpace(n.Name) ? $"Узел #{n.Id}" : n.Name
                    })
                    .ToList();

                ComboStart.ItemsSource = _waypoints;
                ComboEnd.ItemsSource = _waypoints;

                DrawAllNodes();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка загрузки графа: {ex.Message}");
        }
    }

    private void UpdateRoute()
    {
        if (ComboStart.SelectedItem is not NodeDto startNode || ComboEnd.SelectedItem is not NodeDto endNode || _graph == null)
        {
            RouteInfoPanel.IsVisible = false;
            DrawRoute(new List<NodeDto>());
            return;
        }

        bool accessibleOnly = ChkAccessibleRoute.IsChecked ?? false;
        var (path, totalSeconds) = FindShortestPath(startNode.Id, endNode.Id, accessibleOnly);

        if (path.Count > 0)
        {
            RouteInfoPanel.IsVisible = true;
            TimeSpan t = TimeSpan.FromSeconds(totalSeconds);

            TxtTravelTime.Text = t.TotalMinutes >= 1 
                ? $"{t.Minutes} мин {t.Seconds} сек" 
                : $"{t.Seconds} сек";

            DrawRoute(path);
        }
        else
        {
            RouteInfoPanel.IsVisible = true;
            TxtTravelTime.Text = "Путь не найден";
            DrawRoute(new List<NodeDto>());
        }
    }

    private void OnRoutePointSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateRoute();
    }

    private void OnAccessibleRouteChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        UpdateRoute();
    }

    private void OnMapPointPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Polygon { Tag: NodeDto point })
            return;

        var selectedPoint = _waypoints.FirstOrDefault(node => node.Id == point.Id);
        if (selectedPoint == null)
            return;

        if (ComboStart.SelectedItem is not NodeDto)
            ComboStart.SelectedItem = selectedPoint;
        else
            ComboEnd.SelectedItem = selectedPoint;
    }

    private (List<NodeDto> Path, double TotalTime) FindShortestPath(int startId, int targetId, bool accessibleOnly)
    {
        if (_graph == null)
            return (new List<NodeDto>(), 0);

        // если включен режим для инвалидов, лестницы исключаются
        var excludedNodeIds = accessibleOnly
            ? _graph.Nodes.Where(node => node.Type == "stairs").Select(node => node.Id).ToHashSet()
            : new HashSet<int>();

        if (excludedNodeIds.Contains(startId) || excludedNodeIds.Contains(targetId))
            return (new List<NodeDto>(), 0);

        var nodesById = _graph.Nodes
            .Where(node => !excludedNodeIds.Contains(node.Id))
            .ToDictionary(node => node.Id);

        if (!nodesById.ContainsKey(startId) || !nodesById.ContainsKey(targetId))
            return (new List<NodeDto>(), 0);

        var graph = new UndirectedGraph<int, TaggedEdge<int, double>>();
        graph.AddVertexRange(nodesById.Keys);

        foreach (var edge in _graph.Edges)
        {
            if (!nodesById.ContainsKey(edge.From) || !nodesById.ContainsKey(edge.To))
                continue;

            graph.AddEdge(new TaggedEdge<int, double>(edge.From, edge.To, edge.Weight > 0 ? edge.Weight : 1));
        }

        var tryGetPath = graph.ShortestPathsDijkstra(edge => edge.Tag, startId);
        if (!tryGetPath(targetId, out IEnumerable<TaggedEdge<int, double>>? pathEdges))
            return (new List<NodeDto>(), 0);

        var path = new List<NodeDto> { nodesById[startId] };
        var totalTime = 0d;
        var currentId = startId;

        foreach (var edge in pathEdges)
        {
            currentId = edge.Source == currentId ? edge.Target : edge.Source;
            path.Add(nodesById[currentId]);
            totalTime += edge.Tag;
        }

        return (path, totalTime);
    }

    private void DrawAllNodes()
    {
        if (_graph == null) return;

        var overlaysToRemove = MapCanvas.Children.OfType<Polygon>().ToList();
        foreach (var overlay in overlaysToRemove)
            MapCanvas.Children.Remove(overlay);

        int? startId = (ComboStart.SelectedItem as NodeDto)?.Id;
        int? endId = (ComboEnd.SelectedItem as NodeDto)?.Id;

        foreach (var node in _graph.Nodes.Where(n => n.Type == "waypoint"))
        {
            var polygon = _graph.Polygons.FirstOrDefault(p => p.NodeId == node.Id);
            if (polygon == null || polygon.Points.Count < 6 || polygon.Points.Count % 2 != 0)
                continue;

            bool isSelectedRoutePoint = node.Id == startId || node.Id == endId;

            var buildingOverlay = new Polygon
            {
                Points = new Points(polygon.Points
                    .Chunk(2)
                    .Select(pair => new Point(pair[0], pair[1]))),
                Fill = isSelectedRoutePoint
                    ? new SolidColorBrush(Color.FromArgb(50, 255, 0, 0))
                    : Brushes.Transparent,
                Stroke = isSelectedRoutePoint ? Brushes.Red : Brushes.Transparent,
                StrokeThickness = isSelectedRoutePoint ? 3 : 0,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = node
            };

            ToolTip.SetTip(buildingOverlay, node.Name);
            buildingOverlay.PointerPressed += OnMapPointPressed;
            MapCanvas.Children.Add(buildingOverlay);
        }
    }

    private void DrawRoute(List<NodeDto> path)
    {
        var linesToRemove = MapCanvas.Children.OfType<Polyline>().ToList();
        foreach (var line in linesToRemove)
            MapCanvas.Children.Remove(line);

        if (path.Count == 0)
        {
            DrawAllNodes();
            return;
        }

        var polyline = new Polyline
        {
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 4,
            Points = new Points()
        };

        foreach (var node in path)
        {
            polyline.Points.Add(new Point(node.X, node.Y));
        }

        MapCanvas.Children.Add(polyline);
        DrawAllNodes();
    }
}
