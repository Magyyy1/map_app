using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Svg.Skia;
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
    private const double WaypointHitAreaSize = 28;

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
        if (sender is not Ellipse { Tag: NodeDto point })
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
        var distances = new Dictionary<int, double>();
        var previous = new Dictionary<int, int>();
        var nodes = new List<int>();

        // Если включен режим для инвалидов, исключаем узлы с лестницами
        var excludedNodes = new HashSet<int>();
        if (accessibleOnly)
        {
            foreach (var node in _graph!.Nodes)
            {
                if (node.Type == "stairs")
                {
                    excludedNodes.Add(node.Id);
                }
            }
        }

        if (excludedNodes.Contains(startId) || excludedNodes.Contains(targetId))
            return (new List<NodeDto>(), 0);

        foreach (var node in _graph!.Nodes)
        {
            if (excludedNodes.Contains(node.Id)) continue;
            distances[node.Id] = double.MaxValue;
            nodes.Add(node.Id);
        }

        if (!distances.ContainsKey(startId)) return (new List<NodeDto>(), 0);
        distances[startId] = 0;

        while (nodes.Count > 0)
        {
            nodes.Sort((x, y) => distances[x].CompareTo(distances[y]));
            var smallest = nodes[0];
            nodes.Remove(smallest);

            if (smallest == targetId)
            {
                var path = new List<NodeDto>();
                int curr = targetId;

                while (previous.ContainsKey(curr))
                {
                    path.Add(_graph.Nodes.First(n => n.Id == curr));
                    curr = previous[curr];
                }

                path.Add(_graph.Nodes.First(n => n.Id == startId));
                path.Reverse();

                return (path, distances[targetId]);
            }

            if (distances[smallest] == double.MaxValue) break;

            var neighbors = _graph.Edges.Where(e => e.From == smallest || e.To == smallest);
            foreach (var edge in neighbors)
            {
                int neighborId = edge.From == smallest ? edge.To : edge.From;
                if (excludedNodes.Contains(neighborId)) continue;

                double weight = edge.Weight > 0 ? edge.Weight : 1;
                double alt = distances[smallest] + weight;

                if (alt < distances[neighborId])
                {
                    distances[neighborId] = alt;
                    previous[neighborId] = smallest;
                }
            }
        }

        return (new List<NodeDto>(), 0);
    }

    private void DrawAllNodes()
    {
        if (_graph == null) return;

        var markersToRemove = MapCanvas.Children.OfType<Ellipse>().ToList();
        foreach (var marker in markersToRemove)
            MapCanvas.Children.Remove(marker);

        int? startId = (ComboStart.SelectedItem as NodeDto)?.Id;
        int? endId = (ComboEnd.SelectedItem as NodeDto)?.Id;

        foreach (var node in _graph.Nodes.Where(n => n.Type == "waypoint"))
        {
            bool isSelectedRoutePoint = node.Id == startId || node.Id == endId;
            double size = isSelectedRoutePoint ? 16 : 8;

            var hitArea = new Ellipse
            {
                Width = WaypointHitAreaSize,
                Height = WaypointHitAreaSize,
                Fill = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = node
            };

            hitArea.PointerPressed += OnMapPointPressed;
            Canvas.SetLeft(hitArea, node.X - WaypointHitAreaSize / 2);
            Canvas.SetTop(hitArea, node.Y - WaypointHitAreaSize / 2);
            MapCanvas.Children.Add(hitArea);

            var ellipse = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = isSelectedRoutePoint ? Brushes.Yellow : Brushes.Red,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(ellipse, node.X - size / 2);
            Canvas.SetTop(ellipse, node.Y - size / 2);

            MapCanvas.Children.Add(ellipse);
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
