using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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
    private const double MapWidth = 1280;
    private const double MapHeight = 853;

    public MainWindow()
    {
        InitializeComponent();
        BtnBuildRoute.Click += OnBuildRouteClick;
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
                var waypoints = _graph.Nodes
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

                ComboStart.ItemsSource = waypoints;
                ComboEnd.ItemsSource = waypoints;

                DrawAllNodes();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка загрузки графа: {ex.Message}");
        }
    }

    private void OnBuildRouteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ComboStart.SelectedItem is not NodeDto startNode || ComboEnd.SelectedItem is not NodeDto endNode || _graph == null)
            return;

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

        foreach (var node in _graph.Nodes.Where(n => n.Type == "waypoint"))
        {
            var ellipse = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Brushes.Red
            };

            Canvas.SetLeft(ellipse, node.X - 4);
            Canvas.SetTop(ellipse, node.Y - 4);

            MapCanvas.Children.Add(ellipse);
        }
    }

    private void DrawRoute(List<NodeDto> path)
    {
        var linesToRemove = MapCanvas.Children.OfType<Polyline>().ToList();
        foreach (var line in linesToRemove)
            MapCanvas.Children.Remove(line);

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
    }
}