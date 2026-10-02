using Registry = Wysicraft.Core.Registry;
using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using Jint;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using Wysicraft.Core;
using Wysicraft.Models;
using Wysicraft.Packaging;
namespace Wysicraft.Designer;
public partial class MainWindow : Window {

	private Project project = new Project();


	private UiDefinition ui;


	private readonly HashSet<string> selected = new HashSet<string>();


	private readonly History<Project> history;


	private string? folder;


	private bool refreshing;


	private bool dirty;

	// An older .wysicraftproj the person chose to keep saving as it is (they were asked once).
	private bool keepLegacyFile;


	private bool grid = true;


	private string lastNudgeSelection = "";
	private long lastNudgeTime;
	private const double Zoom = 2.0;
	private const double FieldLabelWidth = 132.0; // fits "Dim game behind UI" and similar labels


	private Point dragStart;


	private Dictionary<string, Bounds>? dragBounds;


	private string? editingScript;


	public MainWindow() : this(false) { }

	/// <summary>The editor. With leaderboardCreator it's the leaderboard creator: a window of its own that edits one
	/// leaderboard page on a copy of the project (LeaderboardCreator.cs).</summary>
	internal MainWindow(bool leaderboardCreator)
	{
		creatorMode = leaderboardCreator;
		InitializeComponent();
		ui = project.Screens[0];
		history = new History<Project>(() => project, delegate(Project value)
		{
			project = value;
			ui = ResolveUi(ui);
			dirty = true;
			RefreshAll();
		}, Json.CloneProject);
		BuildMenus();
        InitializeAssetBrowsers(); InitializeComponents();
		InitializeDocking(); InitializeZoom(); InitializeOutsideMarquee();
		if (!creatorMode) InitializeRecovery();
		ScriptEditor.PreviewKeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key == Key.Space && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
			{
				e.Handled = true;
				ShowScriptApi();
			}
		};
		InitializeLayers();
		RebuildToolbox();
		Toolbox.PreviewMouseMove += delegate(object _, MouseEventArgs e)
		{
			if (e.LeftButton == MouseButtonState.Pressed && Toolbox.SelectedItem is ListBoxItem { Tag: string } listBoxItem)
			{
				DragDrop.DoDragDrop(Toolbox, new DataObject("control", listBoxItem.Tag), DragDropEffects.Copy);
			}
		};
		Toolbox.MouseDoubleClick += delegate
		{
			if (Toolbox.SelectedItem is ListBoxItem { Tag: string } listBoxItem)
			{
				AddControl((string)listBoxItem.Tag, 16.0, 16.0);
			}
		};
		Surface.Drop += delegate(object _, DragEventArgs e)
		{
			if (e.Data.GetData("control") is string type)
			{
				Point position = e.GetPosition(Surface);
				AddControl(type, position.X / 2.0, position.Y / 2.0);
			}
		};
        Surface.MouseLeftButtonDown+=(_,e)=>CanvasBackgroundDown(e);
        Surface.MouseMove+=DragMove;
        Surface.MouseLeftButtonUp+=(_,e)=>{if(marqueeStart!=null)UpdateMarquee(e.GetPosition(Surface));EndCanvasGesture();Draw();RefreshInspector();};
		Screens.SelectionChanged += delegate
		{
			if (!refreshing)
			{
				object selectedItem = Screens.SelectedItem;
				string? id = selectedItem as string;
				if (id != null)
				{
					ShowScreen(project.Screens.First((UiDefinition s) => s.Id == id && !s.IsComponent));
				}
			}
		};
		AddScreen.Click += delegate
		{
			Guard(delegate
			{
				string? id = Prompt("New screen", "Screen ID", "screen_" + project.Screens.Count);
				if (id != null)
				{
					if (!Wysicraft.Core.Validation.Id(id) || project.Screens.Any((UiDefinition s) => s.Id == id))
					{
						throw new Exception("Use a unique lowercase ID");
					}
					Change();
					ui = new UiDefinition
					{
						Id = id,
						Title = id
					};
					project.Screens.Add(ui);
					selected.Clear();
					RefreshAll();
				}
			});
		};
		ScreenSettings.Click += (_, _) => Guard(ShowScreenSettings);
		NewScript.Click += delegate
		{
			Guard(delegate
			{
				string? text = Prompt("New script", "Path (scripts/client/name.js or scripts/server/name.js)", "scripts/client/main.js");
				if (text != null)
				{
					ValidateScriptPath(text);
					if (project.Scripts.ContainsKey(text))
					{
						throw new Exception("Script already exists");
					}
					Change();
					project.Scripts[text] = "function onClick(ctx) {\n  // Use only the approved Arcadia Studio API.\n}\n";
					RefreshScripts(text);
				}
			});
		};
		OpenScript.Click += delegate
		{
			Guard(delegate
			{
				OpenFileDialog openFileDialog = new OpenFileDialog
				{
					Filter = "JavaScript|*.js"
				};
				if (openFileDialog.ShowDialog() == true)
				{
					string? text = Prompt("Import script", "Project path", "scripts/client/" + Path.GetFileName(openFileDialog.FileName));
					if (text != null)
					{
						ValidateScriptPath(text);
						Change();
						project.Scripts[text] = File.ReadAllText(openFileDialog.FileName);
						RefreshScripts(text);
					}
				}
			});
		};
		SaveScript.Click += delegate
		{
			Guard(delegate
			{
				SaveScriptText();
				Save(saveAs: false);
			});
		};
		ScriptFiles.SelectionChanged += delegate
		{
			if (!refreshing)
			{
				ScriptTemplate? template = ScriptTemplate.All.FirstOrDefault((ScriptTemplate t) => t.Title == ScriptFiles.SelectedItem as string);
				if (template != null)
				{
					Guard(delegate
					{
						ChooseScriptTemplate(template);
					});
				}
				else
				{
					SaveScriptText();
					editingScript = ScriptFiles.SelectedItem as string;
					ScriptEditor.Text = ((editingScript == null) ? "" : project.Scripts[editingScript]);
					TextBlock scriptSide = ScriptSide;
					string? text = editingScript;
					scriptSide.Text = ((text != null && text.Contains("/server/")) ? "SERVER • trusted host only" : "CLIENT");
				}
			}
		};
		ScriptEditor.TextChanged += delegate
		{
			LineNumbers.Text = string.Join("\n", Enumerable.Range(1, Math.Min(1000, (ScriptEditor.LineCount <= 0) ? 1 : ScriptEditor.LineCount)));
		};
		base.PreviewKeyDown += Keys;
		base.Closing += delegate(object? _, CancelEventArgs e)
		{
			if (!CloseSideEditors()) { e.Cancel = true; return; }
			// The leaderboard creator never asks about saving: closing it hands the page back to the project.
			if (creatorMode) { SaveScriptText(); return; }
			if (!crashRecovery && !closingForUpdate)
			{
				SaveScriptText();
				if (dirty)
				{
					MessageBoxResult num = MessageBox.Show(this, "Save changes before closing?", "Arcadia Studio", MessageBoxButton.YesNoCancel);
					if (num == MessageBoxResult.Cancel)
					{
						e.Cancel = true;
					}
					if (num == MessageBoxResult.Yes)
					{
						Save(saveAs: false);
						e.Cancel = dirty;
					}
				}
			}
		};
		RefreshAll();
	}


	private void Guard(Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			Log(ex.Message);
			MessageBox.Show(this, ex.Message, "Arcadia Studio", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}


	private void Change()
	{
		SaveScriptText();
		history.Checkpoint();
		dirty = true;
		lastNudgeTime = 0; // any other edit ends a nudge run
	}


	internal void Log(string text)
	{
		Output.Items.Add(text);
		ListBox output = Output;
		ItemCollection items = Output.Items;
		output.ScrollIntoView(items[items.Count - 1]);
		Status.Text = text.Split('\n')[0];
	}


	internal void LoadForSmoke(string path)
	{
		project = ProjectStore.Load(path);
		ui = project.Screens[0];
		selected.Add(ui.Elements.First().Id);
		RefreshAll();
		dirty = false;
	}


	internal void Capture(string path)
	{
		UpdateLayout();
		VerifyCanvasSelection();
		VerifyAppearanceTools();
		UpdateLayout();
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap((int)base.ActualWidth, (int)base.ActualHeight, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(this);
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
		using FileStream stream = File.Create(path);
		pngBitmapEncoder.Save(stream);
	}


	private void VerifyCanvasSelection()
	{
		foreach (Element item in ui.Elements.Where((Element e) => e.Type == "button"))
		{
			Point point = new Point((item.Bounds.X + item.Bounds.Width / 2.0) * 2.0, (item.Bounds.Y + item.Bounds.Height / 2.0) * 2.0);
			if (!(Surface.InputHitTest(point) is Border border) || !object.Equals(border.Tag, item.Id))
			{
				var hit = Surface.InputHitTest(point) as FrameworkElement;
				throw new InvalidOperationException($"Canvas did not hit the placed button: {item.Id} (hit {hit?.GetType().Name ?? "nothing"} {hit?.Tag}; selected {string.Join(",", selected)})");
			}
			border.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
			{
				RoutedEvent = Mouse.MouseDownEvent
			});
			if (!selected.Contains(item.Id))
			{
				throw new InvalidOperationException("Canvas did not select: " + item.Id);
			}
			dragBounds = null;
			Surface.ReleaseMouseCapture();
			UpdateLayout();
		}
		dirty = false;
		history.Clear();
	}




	private bool CanReplace()
	{
		if (!CloseSideEditors()) return false; // an open editor would otherwise save into the next project
		SaveScriptText();
		if (!dirty)
		{
			return true;
		}
		switch (MessageBox.Show(this, "Save current project first?", "Arcadia Studio", MessageBoxButton.YesNoCancel))
		{
		case MessageBoxResult.Cancel:
			return false;
		case MessageBoxResult.Yes:
			Save(saveAs: false);
			return !dirty;
		default:
			return true;
		}
	}


	private void NewProject()
	{
		if (!CanReplace())
		{
			return;
		}
		string? text = Prompt("New project", "Project name", "Untitled");
		if (text != null)
		{
			ClearRecovery();
			editingScript = null;
			project = new Project();
			project.Manifest.Name = text;
			// New games keep script state between events once they're made for web & desktop (see Project settings).
			project.Manifest.KeepScriptState = true;
			project.Manifest.Id = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9_]", "_");
			if (!Wysicraft.Core.Validation.Id(project.Manifest.Id))
			{
				project.Manifest.Id = "new_project";
			}
			ui = project.Screens[0];
			folder = null;
			selected.Clear();
			history.Clear();
			dirty = true;
			RefreshAll();
			Settings();
		}
	}


	private void OpenProject()
	{
		if (CanReplace())
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "Arcadia Studio files|*.arcadia;*.wysicraftproj;*.wysicraft;project.json|Arcadia Studio project|*.arcadia|Older project (Wysicraft)|*.wysicraftproj|Legacy project|project.json|Minecraft pack|*.wysicraft"
			};
			if (openFileDialog.ShowDialog() == true)
			{
				string fileName = openFileDialog.FileName;
				OpenProjectPath(fileName);
			}
		}
	}


	private void Save(bool saveAs)
	{
		Guard(delegate
		{
			SaveScriptText();
			string? fileName = folder;
			// A project from before the rename: offer, once, to save it as .arcadia beside the old file.
			if (fileName != null && !saveAs && !keepLegacyFile && fileName.EndsWith(ProjectStore.LegacyExtension, StringComparison.OrdinalIgnoreCase))
			{
				string renamed = Path.ChangeExtension(fileName, ProjectStore.Extension);
				var answer = MessageBox.Show(this, "Save as " + Path.GetFileName(renamed) + "?\n\nArcadia Studio projects are .arcadia files now. The old " + Path.GetFileName(fileName) + " is kept as it is.", "Arcadia Studio", MessageBoxButton.YesNoCancel);
				if (answer == MessageBoxResult.Cancel) return;
				if (answer == MessageBoxResult.No) keepLegacyFile = true;
				else if (File.Exists(renamed)) saveAs = true; // let the save box ask before replacing it
				else fileName = renamed;
			}
			if ((fileName == null) | saveAs)
			{
				SaveFileDialog saveFileDialog = new SaveFileDialog
				{
					Title = "Save Arcadia Studio project",
					Filter = "Arcadia Studio project|*.arcadia",
					DefaultExt = ProjectStore.Extension,
					AddExtension = true,
					FileName = ((folder == null) ? (project.Manifest.Id + ProjectStore.Extension) : Path.ChangeExtension(Path.GetFileName(folder), ProjectStore.Extension))
				};
				if (saveFileDialog.ShowDialog() != true)
				{
					return;
				}
				fileName = saveFileDialog.FileName;
			}
			ProjectStore.SaveProject(project, fileName!);
			folder = fileName;
			dirty = false;
			ClearRecovery();
			Log("Saved " + fileName);
		});
	}



	private void ExportKube()
	{
		SaveScriptText();
		if (!MinecraftExportAllowed("Exporting for Minecraft")) return;
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = "Bundled project JAR|*.jar",
			FileName = project.Manifest.Id + ".jar"
		};
		if (saveFileDialog.ShowDialog() == true)
		{
			Distribution.Write(saveFileDialog.FileName, Distribution.BundledJar(project, RuntimeJar()));
			Log("Exported bundled project JAR: " + saveFileDialog.FileName + ". KubeJS handlers require KubeJS/Rhino installed.");
		}
	}


	private void Validate()
	{
		SaveScriptText();
		Output.Items.Clear();
		List<Issue> list = Wysicraft.Core.Validation.Check(project);
		foreach (Issue item in list)
		{
			Output.Items.Add(item);
		}
		// Advice is listed with the rest but counted apart: it blocks nothing, it just says what something will cost.
		List<Issue> advice = Wysicraft.Core.Validation.Advice(project);
		foreach (Issue note in advice)
		{
			Output.Items.Add(note);
		}
		string andAdvice = (advice.Count > 0) ? $" {advice.Count} note(s) about performance." : "";
		if (list.Count == 0)
		{
			Log("Validation passed." + andAdvice);
		}
		else
		{
			int minecraftOnly = list.Count(i => i.MinecraftOnly);
			Log((minecraftOnly == list.Count ? $"No errors. {minecraftOnly} thing(s) only block Minecraft exports; web and desktop exports are fine." : $"{list.Count - minecraftOnly} validation error(s)" + (minecraftOnly > 0 ? $", plus {minecraftOnly} that only block Minecraft exports" : "")) + andAdvice);
		}
		ShowDock("output");
	}


	private void RefreshAll()
	{
		refreshing = true;
		if (toolboxTarget != ToolboxKey()) { toolboxTarget = ToolboxKey(); RebuildToolbox(); }
		Screens.ItemsSource = project.Screens.Where(s=>!s.IsComponent).Select((UiDefinition s) => s.Id).ToList();
		Screens.SelectedItem = ui.IsComponent || ui.IsLeaderboard ? null : ui.Id;
        RefreshSourceContext();
		refreshing = false;
		RefreshScripts();
        RefreshAssetBrowser(); RefreshComponents();
		Draw();
		RefreshInspector();
		base.Title = creatorMode ? "Leaderboard creator — " + (ui.Title.Length > 0 ? ui.Title : ui.Id) : project.Manifest.Name + " — Arcadia Studio";
	}


	private void AddControl(string type, double x, double y)
	{
		// Toolbox stamps carry a preset after a colon: "shape:star", "collider:circle".
		string preset = ""; int colon = type.IndexOf(':'); if (colon > 0) { preset = type[(colon + 1)..]; type = type[..colon]; }
		// What can go where: a leaderboard page takes page pieces and widgets only, and widgets go nowhere else.
		if (type == "lb") { AddBoardWidget(preset, Snap(x), Snap(y)); return; }
		if (ui.IsLeaderboard && !Leaderboards.Types.Contains(type)) throw new InvalidOperationException("A leaderboard page holds labels, pictures, panels, shapes and leaderboard widgets.");
		if (!ui.IsLeaderboard && type.StartsWith("lb_")) throw new InvalidOperationException("Leaderboard widgets go on leaderboard pages: Advanced → Create leaderboard.");
		if (type == "slots" && preset == "crafting_table") { AddCraftingTable(Snap(x), Snap(y)); return; }
		Change();
		Element element = new Element();
		element.Type = type;
		element.Id = Unique(type);
		element.Value = ((type == "item_list") ? "[]" : "0");
		element.Text = Registry.Controls[type].DisplayName;
		Element element2 = element;
		Bounds bounds = new Bounds();
		bounds.X = Snap(x);
		bounds.Y = Snap(y);
		Bounds bounds2 = bounds;
		bool flag;
		switch (type)
		{
		case "panel":
		case "scroll_panel":
		case "item_list":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		bounds2.Width = (flag ? 180 : 100);
		Bounds bounds3 = bounds;
		bool flag2;
		switch (type)
		{
		case "panel":
		case "scroll_panel":
		case "item_list":
			flag2 = true;
			break;
		default:
			flag2 = false;
			break;
		}
		bounds3.Height = (flag2 ? 100 : 20);
		element2.Bounds = bounds;
		Element element3 = element;
		// Pictures draw their own image; a fill behind them would show through transparent pixels.
		if (type is "image" or "texture_region" or "item" or "sprite" or "tilemap") element3.FillEnabled = false;
		ApplyControlPreset(element3, preset);
		element3.LayerGroup=isolatedGroup;
        ui.Elements.Add(element3);
		selected.Clear();
		selected.Add(element3.Id);
		Draw();
		RefreshInspector();
	}


	// New and copied elements are numbered name1, name2, …: a copy of "button3" becomes the next free
	// "buttonN" instead of growing a "_1" suffix on every copy.
	private string Unique(string basis) => ElementIds.Next(basis, ui.Elements.Select(e => e.Id));


	private double Snap(double n)
	{
		if (!project.Manifest.Snap)
		{
			return Math.Round(n);
		}
		return Math.Round(n / (double)Math.Max(1, project.Manifest.GridSize)) * (double)Math.Max(1, project.Manifest.GridSize);
	}


	private void Draw()
	{
		SyncIsolation(); RefreshSourceContext();
		// A tilemap's box always matches its grid: the runtime draws the grid, so a box that disagreed would only
		// mislead about where the map is and what a click lands on.
		foreach (var map in ui.Elements) Tilemaps.Fit(map);
        Surface.Children.Clear(); handlePlacers.Clear();
		// Pictures as the game scales them: hard pixel edges, or smoothly when the project asks for smooth pictures.
		RenderOptions.SetBitmapScalingMode(Surface, project.Manifest.SmoothImages && project.Manifest.Target != "minecraft" ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
		Surface.Width = (double)ui.Size.Width * 2.0;
		Surface.Height = (double)ui.Size.Height * 2.0;
		if (grid)
		{
			int num = Math.Max(2, project.Manifest.GridSize) * 2;
			DrawingGroup drawingGroup = new DrawingGroup();
			drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(52, 58, 67)), null, new RectangleGeometry(new Rect(0.0, 0.0, num, num))));
			drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(90, 98, 110)), null, new RectangleGeometry(new Rect(0.0, 0.0, 1.0, 1.0))));
			Surface.Background = new DrawingBrush(drawingGroup)
			{
				TileMode = TileMode.Tile,
				ViewportUnits = BrushMappingMode.Absolute,
				Viewport = new Rect(0.0, 0.0, num, num)
			};
		}
		else
		{
			Surface.Background = new SolidColorBrush(Color.FromRgb(52, 58, 67));
		}
		foreach (Element e in ui.Elements.Where(x => x.Type != "sound")) // Sound controls have no picture: they live in Layers
		{
			Border border = new Border
			{
				Background = Brushes.Transparent,
				Tag = e.Id,
				Width = e.Bounds.Width * 2.0,
				Height = e.Bounds.Height * 2.0,
				BorderThickness = new Thickness((!selected.Contains(e.Id)) ? 1 : 2),
				BorderBrush = (selected.Contains(e.Id) ? Brushes.DeepSkyBlue : Brushes.DimGray),
				Opacity = (e.Visible ? Math.Clamp(e.Opacity, 0.15, 1.0) : 0.25),
				Child = RenderControl(e, interactive: false, null),
				ToolTip = e.Id + " • " + e.Type
			};
			Canvas.SetLeft(border, e.Bounds.X * 2.0);
			Canvas.SetTop(border, e.Bounds.Y * 2.0);
			Surface.Children.Add(border);
			Rect rect = new Rect(e.Bounds.X * 2.0, e.Bounds.Y * 2.0, e.Bounds.Width * 2.0, e.Bounds.Height * 2.0);
			foreach (Element item in ContainerTree.Ancestors(ui, e))
			{
				rect.Intersect(new Rect(item.Bounds.X * 2.0, item.Bounds.Y * 2.0, item.Bounds.Width * 2.0, item.Bounds.Height * 2.0));
			}
			border.Clip = new RectangleGeometry(rect.IsEmpty ? default(Rect) : new Rect(rect.X - e.Bounds.X * 2.0, rect.Y - e.Bounds.Y * 2.0, rect.Width, rect.Height));
			// Rotation and scale (web & desktop), as the game draws them: the control about its own centre, and inside a
			// turned panel along with it. The clip is the panels' area, which the control's own turn must not carry off.
			if (CanvasTransform(e, out Transform? ownTurn) is Transform turn)
			{
				border.RenderTransform = turn;
				if (ownTurn != null)
				{
					// The control's own box turns with it; the area of the panels it is attached inside does not.
					var box = new RectangleGeometry(new Rect(0.0, 0.0, e.Bounds.Width * 2.0, e.Bounds.Height * 2.0));
					Rect? area = null;
					foreach (Element panel in ContainerTree.Ancestors(ui, e))
					{
						var within = new Rect((panel.Bounds.X - e.Bounds.X) * 2.0, (panel.Bounds.Y - e.Bounds.Y) * 2.0, panel.Bounds.Width * 2.0, panel.Bounds.Height * 2.0);
						if (area is Rect so) { so.Intersect(within); area = so; } else area = within;
					}
					if (area is not Rect inside) border.Clip = box;
					else if (ownTurn.Value.HasInverse) { var back = ownTurn.Value; back.Invert(); border.Clip = new CombinedGeometry(GeometryCombineMode.Intersect, box, new RectangleGeometry(inside.IsEmpty ? default(Rect) : inside) { Transform = new MatrixTransform(back) }); }
				}
			}
			// Filled when it's actually opened: building a full menu for every control on every redraw was the bulk of a redraw.
			ContextMenu menu = new ContextMenu();
			border.ContextMenu = menu;
			border.ContextMenuOpening += delegate { FillElementMenu(menu, e); };
			menu.Opened += delegate { if (menu.Items.Count == 0) FillElementMenu(menu, e); };
            if(!InIsolation(e)){border.Opacity*=.25;border.IsHitTestVisible=false;}
            if(e.Locked)border.IsHitTestVisible=false; // clicks pass through to what's underneath
            if(e.Type=="camera"){border.Background=null;border.BorderThickness=new Thickness(selected.Contains(e.Id)?3:2);} // a camera is grabbed by its edge; clicks inside reach the controls under it
            border.MouseLeftButtonDown+=(_,args)=>CanvasElementDown(e,args);
			if (selected.Contains(e.Id) && InIsolation(e) && !e.Locked) AddResizeHandles(e);
		}
		DrawAnchorMarkers();
		DrawMarquee();
        RefreshLayers();
		Status.Text = StatusLine();
	}


	private void DragMove(object sender, MouseEventArgs args)
    {
        if(marqueeStart!=null){if(args.LeftButton==MouseButtonState.Pressed)UpdateMarquee(args.GetPosition(Surface));return;}

		if (dragBounds == null || args.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
        UpdateElementDrag(args.GetPosition(Surface));
    }

    void UpdateElementDrag(Point position) {
        if(dragBounds==null)return;
        if(!dragChanged){var delta=position-dragStart;if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance)return;Change();dragChanged=true;lastCanvasClick=0;}
        foreach(var item in ui.Elements.Where(e=>dragBounds.ContainsKey(e.Id))){item.Bounds.X=Math.Max(0,Snap(dragBounds[item.Id].X+(position.X-dragStart.X)/Zoom));item.Bounds.Y=Math.Max(0,Snap(dragBounds[item.Id].Y+(position.Y-dragStart.Y)/Zoom));}
        // Moving items only changes where they are, so the canvas and Layers aren't rebuilt until the drag ends.
        UpdateCanvasBounds(dragBounds.Keys);
        MoveCanvasOverlays();
    }


	private void Delete()
	{
		if (selected.Count != 0)
		{
			Change();
			HashSet<string> removed = (from e in ContainerTree.Moving(ui, selected)
				select e.Id).ToHashSet();
			ui.Elements.RemoveAll((Element e) => removed.Contains(e.Id));
            ui.ComponentInstances.RemoveAll(i=>removed.Contains(i.Root));
			selected.Clear();
			Draw();
			RefreshInspector();
		}
	}


	private void Copy()
	{
		List<Element> list = ContainerTree.Moving(ui, selected).ToList();
		if (list.Count > 0)
		{
			Clipboard.SetData("Wysicraft.ElementsV2", Json.Write(new UiDefinition
			{
				Elements = list,
				GroupParents = new Dictionary<string, string>(ui.GroupParents)
			}));
		}
	}


	private void Paste()
	{
		if (Clipboard.GetData("Wysicraft.ElementsV2") is string value)
		{
			UiDefinition uiDefinition = Json.Read<UiDefinition>(value);
			InsertCopies(uiDefinition.Elements, uiDefinition.GroupParents);
		}
		else if (Clipboard.GetData("Wysicraft.Elements") is string value2)
		{
			InsertCopies(Json.Read<List<Element>>(value2));
		}
	}


    private void SelectCanvasElement(string id,ModifierKeys modifiers) {
        var element=ui.Elements.First(e=>e.Id==id);if(!InIsolation(element))return;
        string group=modifiers.HasFlag(ModifierKeys.Alt)?"":CanvasGroup(element);
        var ids=group.Length==0?new[]{id}:ui.Elements.Where(e=>InIsolation(e)&&LayerGroups.Contains(ui,group,e.LayerGroup)).Select(e=>e.Id).ToArray();
        bool additive=(modifiers&(ModifierKeys.Control|ModifierKeys.Shift))!=0;
        bool remove=additive&&ids.All(selected.Contains);if(!additive)selected.Clear();
        foreach(var key in ids)if(remove)selected.Remove(key);else selected.Add(key);
    }

	private void Keys(object sender, KeyEventArgs e)
	{
        if(e.Key==Key.Escape && (isolatedGroup.Length>0 || marqueeStart!=null)){if(isolatedGroup.Length>0)ExitIsolation();else{EndCanvasGesture();Draw();}e.Handled=true;return;}
		if (e.Handled)
		{
			return;
		}
		// Every command shortcut comes from the user's bindings (View → Keyboard shortcuts).
		if (TryRunShortcut(e))
		{
			e.Handled = true;
			return;
		}
		if (e.OriginalSource is TextBox || e.OriginalSource is ComboBox)
		{
			return;
		}
		// Arrow keys nudge the selection (Shift: 10 px). Locked items stay put unless their panel moves.
		if (selected.Count == 0 || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down) || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
		{
			return;
		}
		var nudgeRoots = selected.Where(id => ui.Elements.FirstOrDefault(x => x.Id == id) is Element n && !n.Locked).ToList();
		if (nudgeRoots.Count == 0)
		{
			return;
		}
		// A run of nudges on the same selection is one undo step (and one snapshot, not one per key repeat).
		string nudgeKey = string.Join("|", selected.OrderBy(s => s, StringComparer.Ordinal));
		long nudgeNow = Environment.TickCount64;
		if (nudgeKey != lastNudgeSelection || nudgeNow - lastNudgeTime > 1000 || history.UndoCount == 0) Change(); else dirty = true;
		lastNudgeSelection = nudgeKey; lastNudgeTime = nudgeNow;
		int num = ((!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) ? 1 : 10);
		foreach (Element item in ContainerTree.Moving(ui, nudgeRoots))
		{
			item.Bounds.X += ((e.Key == Key.Left) ? (-num) : ((e.Key == Key.Right) ? num : 0));
			item.Bounds.Y += ((e.Key == Key.Up) ? (-num) : ((e.Key == Key.Down) ? num : 0));
		}
		Draw();
		RefreshInspector();
		e.Handled = true;
	}


	private void Heading(StackPanel panel, string text)
	{
		// In Properties a heading is later folded into a collapsible section (FoldSections); the tag is how it is found.
		panel.Children.Add(new TextBlock
		{
			Text = text.ToUpperInvariant(),
			Foreground = Brushes.LightSkyBlue,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(4.0, 12.0, 4.0, 5.0),
			Tag = panel == Properties ? new SectionHeading(text) : null
		});
	}


	private void Field(StackPanel panel, string label, object obj, string name)
	{
		bool flag;
		switch (name)
		{
		case "Foreground":
		case "Background":
		case "BorderColor":
		case "ShadowColor":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			ColorField(panel, label, obj, name);
			return;
		}
		PropertyInfo prop = obj.GetType().GetProperty(name)!;
		DockPanel dockPanel = new DockPanel();
		dockPanel.Children.Add(new TextBlock
		{
			Text = label,
			Width = FieldLabelWidth,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(4.0)
		});
		TextBox box;
		string old;
		bool checkpoint;
		UiDefinition? layoutBase = null;
		if (prop.PropertyType == typeof(bool))
		{
			CheckBox check = new CheckBox
			{
				IsChecked = (bool)prop.GetValue(obj)!
			};
			check.Click += delegate
			{
				Change();
				prop.SetValue(obj, check.IsChecked == true);
				Draw();
			};
			dockPanel.Children.Add(check);
		}
		else
		{
			object? value = prop.GetValue(obj);
			box = new TextBox
			{
				Text = ((value is List<string> values) ? string.Join("|", values) : (Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""))
			};
			old = box.Text ?? "";
			checkpoint = false;
			box.TextChanged += delegate
			{
				Apply(report: false);
			};
			box.LostKeyboardFocus += delegate
			{
				Apply(report: true);
				checkpoint = false;
			};
			box.KeyDown += delegate(object _, KeyEventArgs e)
			{
				if (e.Key == Key.Return)
				{
					Apply(report: true);
					checkpoint = false;
					e.Handled = true;
				}
			};
			dockPanel.Children.Add(box);
		}
		panel.Children.Add(dockPanel);
		void Apply(bool report)
		{
			string text = box.Text ?? "";
			if (text == old)
			{
				return;
			}
			try
			{
				object obj2 = ((prop.PropertyType == typeof(List<string>)) ? text.Split('|').ToList() : Convert.ChangeType(text, prop.PropertyType, CultureInfo.InvariantCulture));
				if (name == "Id" && (!Wysicraft.Core.Validation.Id(text) || (obj is Element && ui.Elements.Any((Element e) => e != obj && e.Id == text))))
				{
					throw new Exception("ID must be unique and lowercase");
				}
				double num = default(double);
				int num2;
				if (obj2 is double)
				{
					num = (double)obj2;
					num2 = 1;
				}
				else
				{
					num2 = 0;
				}
				bool flag2 = (byte)num2 != 0;
				if (flag2)
				{
					bool flag3 = !double.IsFinite(num) || Math.Abs(num) > 4096.0;
					if (!flag3)
					{
						string text2 = name;
						bool flag4 = ((text2 == "Width" || text2 == "Height") ? true : false);
						flag3 = flag4 && num < 1.0;
					}
					flag2 = flag3 || (name == "Opacity" && (num < 0.0 || num > 1.0)) || (name == "FontScale" && (num <= 0.0 || num > 8.0)) || (name == "Scale" && (num < 0.01 || num > 100.0));
				}
				if (flag2)
				{
					throw new Exception("Value outside supported range");
				}
				int num3 = default(int);
				int num4;
				if (obj is Wysicraft.Models.Size)
				{
					if (obj2 is int)
					{
						num3 = (int)obj2;
						num4 = 1;
					}
					else
					{
						num4 = 0;
					}
				}
				else
				{
					num4 = 0;
				}
				flag2 = (byte)num4 != 0;
				if (flag2)
				{
					bool flag3 = ((num3 < 16 || num3 > 4096) ? true : false);
					flag2 = flag3;
				}
				if (flag2)
				{
					throw new Exception("Canvas size must be 16–4096");
				}
				if (name == "Parent" && obj is Element element)
				{
					string parent = element.Parent;
					element.Parent = text;
					try
					{
						ContainerTree.Ancestors(ui, element).ToArray();
					}
					finally
					{
						element.Parent = parent;
					}
				}
				if (!checkpoint)
				{
					Change();
					checkpoint = true;
					layoutBase = Json.Clone(ui);
				}
				Bounds? bounds = obj as Bounds;
				flag2 = bounds != null;
				if (flag2)
				{
					string text2 = name;
					bool flag3 = ((text2 == "X" || text2 == "Y") ? true : false);
					flag2 = flag3;
				}
				if (flag2)
				{
					Element? owner = ui.Elements.FirstOrDefault((Element e) => e.Bounds == bounds);
					if (owner != null)
					{
						double num5 = Convert.ToDouble(obj2) - Convert.ToDouble(prop.GetValue(obj));
						foreach (Element item in from e in ContainerTree.Moving(ui, new string[1] { owner.Id })
							where e != owner
							select e)
						{
							if (name == "X")
							{
								item.Bounds.X += num5;
							}
							else
							{
								item.Bounds.Y += num5;
							}
						}
					}
				}
				if (name == "Id")
				{
					if (obj is Element element2)
					{
						string oldId = element2.Id;
						ReportScriptMentions(oldId, ElementIds.Rename(project, ui, oldId, text));
						selected.Remove(oldId);
						selected.Add(text);
					}
				}
				prop.SetValue(obj, obj2);
				if (layoutBase != null) ReflowAfterSizeEdit(layoutBase, obj, name);
				old = text;
				box.BorderBrush = new SolidColorBrush(Color.FromRgb(69, 75, 86));
				Draw();
			}
			catch (Exception ex)
			{
				box.BorderBrush = Brushes.IndianRed;
				if (report)
				{
					Log(ex.Message);
					box.Text = old;
				}
			}
		}
	}


	private void RefreshInspector()
	{
		BuildInspector();
		FoldSections();
	}

	private void BuildInspector()
	{
		Properties.Children.Clear();
		Events.Children.Clear();
		Element? element = ui.Elements.FirstOrDefault((Element e) => selected.Contains(e.Id));
		// Ask Agent sits above everything, including Identity: it is about the thing as a whole, not one field of it.
		AddAskAgent(Properties, element);
		if (element == null && ui.IsLeaderboard) { BuildLeaderboardSettings(); return; }
		if (element != null && ui.IsLeaderboard) { BuildLeaderboardElementInspector(element); return; }
		if (element == null)
		{
			BuildComponentFields(null); BuildScreenSettings();
			if(!ui.IsComponent)BuildEvents(ui.Events, EventNames(Registry.ScreenEvents, true));
            else Events.Children.Add(new TextBlock {Text="Component sources have control events. Configure screen open/close on the destination screen.",TextWrapping=TextWrapping.Wrap});
			return;
		}
		Heading(Properties, "Identity");
		string[] array = new string[2] { "Id", "Name" };
		foreach (string text in array)
		{
			Field(Properties, text, element, text);
		}
		Heading(Properties, "Layout");
		array = new string[4] { "X", "Y", "Width", "Height" };
		foreach (string text2 in array)
		{
			Field(Properties, text2, element.Bounds, text2);
		}
		BuildParentField(element);
		BuildAnchorFields(element); BuildComponentFields(element);
		BuildAppearance(element);
		Heading(Properties, "Behavior");
		array = new string[5] { "Visible", "Enabled", "Tooltip", "VisibleIf", "EnabledIf" };
		foreach (string text3 in array)
		{
			Field(Properties, text3, element, text3);
		}
		if (Registry.Controls.TryGetValue(element.Type, out ControlSpec? value))
		{
			Heading(Properties, "Control / Minecraft");
			array = value.Properties;
			foreach (string text4 in array)
			{
				if (!SpecialField(element, text4)) Field(Properties, text4, element, text4);
			}
			BuildAdvancedFields(element);
			BuildEvents(element.Events, EventNames(value.Events, false));
		}
        if(element.Type=="item") {var picker=new Button {Content="Browse Minecraft items"};picker.Click+=(_,_)=>ShowDock("items");Properties.Children.Add(picker);}
        BuildRowTemplateFields(element);
	}


	private void BuildEvents(Dictionary<string, UiEvent> events, string[] names)
	{
		Heading(Events, (events == ui.Events) ? ("Screen: " + ui.Id) : ("Element: " + ui.Elements.First((Element e) => e.Events == events).Id));
		Heading(Events, "Event actions");
		ComboBox pick = new ComboBox
		{
			ItemsSource = names,
			SelectedIndex = 0
		};
		ComboBox side = new ComboBox
		{
			ItemsSource = new string[2] { "Client", "Server" },
			SelectedIndex = ((events.TryGetValue(names[0], out UiEvent? value) && value.Server.Script.Length > 0 && value.Client.Script.Length == 0) ? 1 : 0)
		};
		Events.Children.Add(pick);
		Events.Children.Add(side);
		StackPanel content = new StackPanel();
		Events.Children.Add(content);
		pick.SelectionChanged += delegate
		{
			Populate();
		};
		side.SelectionChanged += delegate
		{
			Populate();
		};
		Populate();
		void Populate()
		{
			content.Children.Clear();
			string name = (string)pick.SelectedItem;
			// Tick and Key only ever run on the player's screen, so their Server side is locked.
			bool clientOnly = ClientOnlyEvent(name);
			side.IsEnabled = !clientOnly;
			if (clientOnly && side.SelectedIndex != 0) { side.SelectedIndex = 0; return; }
			bool server = side.SelectedIndex == 1;
			Wysicraft.Models.EventHandler? h = ((!events.TryGetValue(name, out UiEvent? value2)) ? null : (server ? value2.Server : value2.Client));
			content.Children.Add(new TextBlock
			{
				Text = name == "tick" ? (ui.TickInterval >= 50 ? $"Runs on the player's screen every {ui.TickInterval} ms (Tick interval in Screen settings)." : "Runs on the player's screen on a timer. Set a Tick interval in Screen settings to turn it on.")
					: AdvancedEventHelp(name) is string advancedHelp ? advancedHelp
					: name == "key" ? "Runs on the player's screen for each key press while no text box is being typed in. The script's value is the key name: a–z, 0–9, space, enter, tab, backspace, left, right, up, down, f1–f12. Escape always closes the screen."
					: (server ? "Trusted server actions. Preview simulates these." : "Runs locally for this screen."),
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(4.0)
			});
			if (server)
			{
				ComboBox permission = new ComboBox
				{
					ItemsSource = new string[5] { "0 — Everyone", "1 — Moderator", "2 — Operator", "3 — Administrator", "4 — Owner" },
					SelectedIndex = (h?.PermissionLevel ?? 0),
					Margin = new Thickness(4.0)
				};
				permission.SelectionChanged += delegate
				{
					Change();
					Ensure().PermissionLevel = permission.SelectedIndex;
				};
				content.Children.Add(permission);
				TextBox cooldown = new TextBox
				{
					Text = (h?.CooldownTicks ?? 4).ToString(),
					Margin = new Thickness(4.0),
					ToolTip = "Server cooldown in ticks (20 ticks = one second). Shared across reopening this screen. Continuous text/value changes are exempt."
				};
				content.Children.Add(new TextBlock
				{
					Text = "Minimum interval (ticks)",
					Margin = new Thickness(4.0)
				});
				content.Children.Add(cooldown);
				cooldown.LostKeyboardFocus += delegate
				{
					if (int.TryParse(cooldown.Text, out var result) && result >= 0 && result <= 1200)
					{
						Change();
						Ensure().CooldownTicks = result;
					}
					else
					{
						cooldown.Text = (h?.CooldownTicks ?? 4).ToString();
					}
				};
			}
			if (h != null)
			{
				foreach (VisualAction action in h.Actions.ToList())
				{
					StackPanel stackPanel = new StackPanel
					{
						Margin = new Thickness(2.0, 8.0, 2.0, 4.0)
					};
					ComboBox type = new ComboBox
					{
						ItemsSource = (server ? Registry.ServerActions : Registry.ClientActions).Order().ToArray(),
						SelectedItem = action.Type
					};
					type.SelectionChanged += delegate
					{
						if (type.SelectedItem is string type2)
						{
							Change();
							action.Type = type2;
							if (type2 == "play_sound") Dispatcher.BeginInvoke(Populate); // show the sound picker
						}
					};
					stackPanel.Children.Add(type);
					if (action.Type == "play_sound") SoundPicker(stackPanel, "Sound", action.Value, v => action.Value = v, "Plays when this event fires.");
					else
					{
						Field(stackPanel, "Target / Variable", action, "Target");
						Field(stackPanel, "Value / Command", action, "Value");
					}
					StackPanel stackPanel2 = new StackPanel
					{
						Orientation = Orientation.Horizontal
					};
					(string, int)[] array = new(string, int)[3]
					{
						("↑", -1),
						("↓", 1),
						("Remove", 0)
					};
					for (int num = 0; num < array.Length; num++)
					{
						(string, int) tuple = array[num];
						string item = tuple.Item1;
						int delta = tuple.Item2;
						Button button = new Button
						{
							Content = item
						};
						button.Click += delegate
						{
							Change();
							int num2 = h.Actions.IndexOf(action);
							if (delta == 0)
							{
								h.Actions.Remove(action);
							}
							else
							{
								int index = Math.Clamp(num2 + delta, 0, h.Actions.Count - 1);
								h.Actions.RemoveAt(num2);
								h.Actions.Insert(index, action);
							}
							Populate();
						};
						stackPanel2.Children.Add(button);
					}
					stackPanel.Children.Add(stackPanel2);
					content.Children.Add(stackPanel);
				}
			}
			Button button2 = new Button
			{
				Content = "+ Add action"
			};
			button2.Click += delegate
			{
				Change();
				Ensure().Actions.Add(new VisualAction
				{
					Type = (server ? "command" : "set_text")
				});
				Populate();
			};
			content.Children.Add(button2);
			BuildEventScripts(content, events, name, server, Populate);
			Wysicraft.Models.EventHandler Ensure()
			{
				if (!events.TryGetValue(name, out UiEvent? value3))
				{
					value3 = (events[name] = new UiEvent());
				}
				if (!server)
				{
					return value3.Client;
				}
				return value3.Server;
			}
		}
	}


	public static string? Prompt(string title, string label, string initial)
	{
		Window window = new Window
		{
			Title = title,
			Width = 480.0,
			Height = 170.0,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Owner = Application.Current.MainWindow,
			ResizeMode = ResizeMode.NoResize
		};
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(12.0)
		};
		TextBox textBox = new TextBox
		{
			Text = initial
		};
		Button button = new Button
		{
			Content = "OK",
			IsDefault = true,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		button.Click += delegate
		{
			window.DialogResult = true;
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = label
		});
		stackPanel.Children.Add(textBox);
		stackPanel.Children.Add(button);
		window.Content = stackPanel;
		textBox.SelectAll();
		textBox.Focus();
		if (window.ShowDialog() != true)
		{
			return null;
		}
		return textBox.Text;
	}


	private void Settings()
	{
		Manifest clone = Json.Clone(project.Manifest);
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(12.0)
		};
		string[] array = new string[8] { "Name", "Id", "Author", "Version", "RuntimeVersion", "GridSize", "Snap", "Dependencies" };
		foreach (string text in array)
		{
			Field(stackPanel, text, clone, text);
		}
		// Made for: which limits and tools apply. Minecraft hides the Advanced toolbox; Web & desktop uses the larger
		// limits without Minecraft warnings; Both allows everything and flags what Minecraft can't run.
		var targets = new (string Key, string Label)[] { ("both", "Minecraft and web & desktop"), ("minecraft", "Minecraft only"), ("web", "Web & desktop only") };
		var targetRow = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
		targetRow.Children.Add(new TextBlock { Text = "Made for", Width = FieldLabelWidth, VerticalAlignment = VerticalAlignment.Center });
		var target = new ComboBox { ItemsSource = targets.Select(t => t.Label).ToArray(), SelectedIndex = Math.Max(0, Array.FindIndex(targets, t => t.Key == clone.Target)) };
		target.SelectionChanged += (_, _) => clone.Target = targets[target.SelectedIndex].Key;
		targetRow.Children.Add(target); stackPanel.Children.Add(targetRow);
		stackPanel.Children.Add(new TextBlock { Text = "Web & desktop projects can use the Advanced toolbox and bigger screens. Minecraft exports list anything Minecraft can't run.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 8) });
		// Kept script state: only for web & desktop, where a game is one player in one browser.
		// Smooth pictures: for illustrated art. Web & desktop only: Minecraft always draws hard pixel edges.
		var smooth = new CheckBox { Content = "Smooth pictures (for illustrated art, not pixel art)", IsChecked = clone.SmoothImages, Margin = new Thickness(0, 2, 0, 8),
			ToolTip = "Pictures are scaled smoothly instead of with hard pixel edges. Leave it off for pixel art, which should stay crisp. Web & desktop only." };
		smooth.Checked += (_, _) => clone.SmoothImages = true; smooth.Unchecked += (_, _) => clone.SmoothImages = false;
		target.SelectionChanged += (_, _) => smooth.IsEnabled = clone.Target != "minecraft"; smooth.IsEnabled = clone.Target != "minecraft";
		stackPanel.Children.Add(smooth);
		var keep = new CheckBox { Content = "Scripts keep their variables between events", IsChecked = clone.KeepScriptState, Margin = new Thickness(0, 2, 0, 2),
			ToolTip = "Each script runs from the top once; after that only the event's function is called, so top-level variables (let score = 0) last until the game starts over. Web & desktop only: Minecraft runs every event from the top." };
		keep.Checked += (_, _) => clone.KeepScriptState = true; keep.Unchecked += (_, _) => clone.KeepScriptState = false;
		var keepNote = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 8) };
		void KeepShown() { bool web = clone.Target == "web"; keep.IsEnabled = web; keepNote.Text = web ? "Top-level variables in scripts last from one event to the next. Keep anything the game should save or show in Preview's variables in ctx.state or ctx.save." : "Only for Web & desktop projects: Minecraft runs each event's script from the top, so keep state in ctx.state."; }
		target.SelectionChanged += (_, _) => KeepShown(); KeepShown();
		stackPanel.Children.Add(keep); stackPanel.Children.Add(keepNote);
		// Game variables: the whole game's, in the same name=value;name=value form as a screen's variables.
		TextBox Row(string label, string text, string tip)
		{
			var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
			row.Children.Add(new TextBlock { Text = label, Width = FieldLabelWidth, VerticalAlignment = VerticalAlignment.Center, ToolTip = tip });
			var box = new TextBox { Text = text, ToolTip = tip }; row.Children.Add(box); stackPanel.Children.Add(row); return box;
		}
		var gameVars = Row("Game variables", string.Join(";", clone.GameVariables.Select(v => v.Key + "=" + v.Value)), "Variables of the whole game, as name=value;name=value. They keep their value when another screen opens, so a Game over screen can show ${score}. Web & desktop.");
		var savedVars = Row("Saved between visits", string.Join(";", clone.SavedVariables), "Which game variables are kept after the game closes, as name;name (a best score, settings). Web & desktop.");
		stackPanel.Children.Add(new TextBlock { Text = "Game variables work like screen variables but carry across screens; saved ones come back next time the game opens.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 8) });
		Window window = new Window
		{
			Title = "Project settings",
			Width = 490.0,
			Height = 740.0,
			Owner = this,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Content = stackPanel
		};
		Button button = new Button
		{
			Content = "Apply",
			IsDefault = true
		};
		button.Click += delegate
		{
			// Game variables from their text fields; a bad name is said at once rather than left for Validate.
			var variables = new Dictionary<string, string>(); var savedNames = new List<string>(); string? bad = null;
			foreach (var pair in gameVars.Text.Split(';', StringSplitOptions.RemoveEmptyEntries)) { var parts = pair.Split('=', 2); string name = parts[0].Trim(); if (!Wysicraft.Core.Validation.Variable(name)) { bad ??= "Game variables: \"" + name + "\" isn't a valid variable name."; continue; } variables[name] = parts.Length > 1 ? parts[1] : ""; }
			foreach (var name in savedVars.Text.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).Distinct()) { if (!variables.ContainsKey(name)) { bad ??= "Saved between visits: " + name + " isn't one of the game variables."; continue; } savedNames.Add(name); }
			if (bad != null) { MessageBox.Show(bad); return; }
			clone.GameVariables = variables; clone.SavedVariables = savedNames;
			bool flag = !Wysicraft.Core.Validation.Id(clone.Id) || !Wysicraft.Core.Validation.Version(clone.Version) || !Wysicraft.Core.Validation.Version(clone.RuntimeVersion);
			if (!flag)
			{
				int gridSize = clone.GridSize;
				bool flag2 = ((gridSize < 1 || gridSize > 128) ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				MessageBox.Show("Check ID, versions and grid size (1–128).");
			}
			else
			{
				Change();
				string oldId = project.Manifest.Id;
				project.Manifest = clone;
				// Keeps images, sounds and fonts working when the Id changes; scripts are text, so the ones naming the old
				// Id are listed for the person to change.
				var scripts = ProjectEdits.MoveAssetNamespace(project, oldId);
				window.Close();
				RefreshAll();
				if (scripts.Count > 0)
					MessageBox.Show(this, $"Images, sounds and fonts now use the ID \"{project.Manifest.Id}\". These scripts still name files as \"{oldId}:…\" and need changing to \"{project.Manifest.Id}:…\":\n\n" + string.Join("\n", scripts) + "\n\nValidate lists each one.", "Project ID changed", MessageBoxButton.OK, MessageBoxImage.Information);
			}
		};
		stackPanel.Children.Add(button);
		window.ShowDialog();
	}




	private void ImportTexture() {ShowDock("assets");ImportBrowserImages();}


	private void ValidateScriptPath(string path)
	{
		Wysicraft.Core.Validation.SafePath(path);
		if ((!path.StartsWith("scripts/client/") && !path.StartsWith("scripts/server/")) || !path.EndsWith(".js"))
		{
			throw new Exception("Use scripts/client/name.js or scripts/server/name.js");
		}
	}


	private void SaveScriptText()
	{
		if (editingScript != null && project.Scripts.ContainsKey(editingScript) && project.Scripts[editingScript] != ScriptEditor.Text)
		{
			history.Checkpoint();
			project.Scripts[editingScript] = ScriptEditor.Text;
			dirty = true;
		}
	}


	private void RefreshScripts(string? pick = null)
	{
		refreshing = true;
		ScriptFiles.ItemsSource = project.Scripts.Keys.Concat(ScriptTemplate.All.Select((ScriptTemplate t) => t.Title)).ToList();
		ScriptFiles.SelectedItem = pick ?? editingScript;
		refreshing = false;
		editingScript = ScriptFiles.SelectedItem as string;
		ScriptEditor.Text = ((editingScript != null) ? project.Scripts[editingScript] : "");
	}


	private static Brush Brush(string value)
	{
		try
		{
			return (Brush)new BrushConverter().ConvertFromString(value)!;
		}
		catch
		{
			return Brushes.Magenta;
		}
	}


	private FrameworkElement RenderControl(Element e, bool interactive, Action<string>? fire)
	{
		FrameworkElement frameworkElement;
		switch (e.Type)
		{
		case "button":
		{
			Button button = new Button();
			button.Content = StyledText(e);
			button.Template = SkinTemplate(e);
			button.Background = SkinBrush(e);
			button.Margin = new Thickness(0.0);
			button.Padding = new Thickness(2.0);
			button.Click += delegate
			{
				fire?.Invoke("click");
			};
			frameworkElement = button;
			break;
		}
		case "textbox":
		{
			TextBox input = new TextBox
			{
				Text = e.Value,
				Margin = new Thickness(0.0)
			};
			input.TextChanged += delegate
			{
				e.Value = input.Text;
				fire?.Invoke("text_changed");
			};
			input.KeyDown += delegate(object _, KeyEventArgs args)
			{
				if (args.Key == Key.Return)
				{
					fire?.Invoke("submit");
				}
			};
			frameworkElement = input;
			break;
		}
		case "checkbox":
		{
			CheckBox check = new CheckBox
			{
				Content = e.Text,
				IsChecked = (e.Value == "true"),
				VerticalAlignment = VerticalAlignment.Center
			};
			check.Click += delegate
			{
				e.Value = ((check.IsChecked == true) ? "true" : "false");
				fire?.Invoke((check.IsChecked == true) ? "checked" : "unchecked");
			};
			frameworkElement = check;
			break;
		}
		case "slider":
		{
			Slider slider = new Slider
			{
				Minimum = e.Minimum,
				Maximum = Math.Max(e.Minimum + 1.0, e.Maximum),
				Value = (double.TryParse(e.Value, out var result3) ? result3 : 0.0)
			};
			slider.ValueChanged += delegate
			{
				e.Value = slider.Value.ToString(CultureInfo.InvariantCulture);
				fire?.Invoke("value_changed");
			};
			frameworkElement = slider;
			break;
		}
		case "progress":
		{
			frameworkElement = new ProgressBar
			{
				Minimum = e.Minimum,
				Maximum = Math.Max(e.Minimum + 1.0, e.Maximum),
				Value = (double.TryParse(e.Value, out var result) ? result : 0.0)
			};
			break;
		}
		case "dropdown":
		{
			ComboBox combo = new ComboBox
			{
				ItemsSource = e.Options,
				SelectedIndex = (int.TryParse(e.Value, out var result2) ? Math.Clamp(result2, 0, Math.Max(0, e.Options.Count - 1)) : 0)
			};
			combo.SelectionChanged += delegate
			{
				e.Value = combo.SelectedIndex.ToString();
				fire?.Invoke("value_changed");
			};
			frameworkElement = combo;
			break;
		}
		case "image":
		case "texture_region":
		{
			if (TryTexture(e.Texture, out byte[] bytes))
			{
				frameworkElement = e.Type == "image" ? TextureImage(e.Texture, bytes) : new Image { Source = DecodeTexture(bytes), Stretch = Stretch.Fill };
			}
			else
			{
				frameworkElement = new TextBlock
				{
					Text = "▧ " + e.Texture,
					Foreground = Brushes.LightGray
				};
			}
			break;
		}
		case "panel":
		case "scroll_panel":
			frameworkElement = new Grid();
			break;
		case "item_list":
		{
			ListBox list = new ListBox
			{
				Background = Brush(e.Background),
				BorderThickness = new Thickness(0.0)
			};
			ScrollViewer.SetCanContentScroll(list, canContentScroll: false);
			FillItemList(list, e.Value, e, fire);
			list.SelectionChanged += delegate
			{
				if (list.SelectedIndex >= 0)
				{
					e.Text = list.SelectedIndex.ToString();
					fire?.Invoke("item_click");
				}
			};
			frameworkElement = list;
			break;
		}
        case "sprite": case "shape": case "collider": case "camera": case "particles": case "tilemap": case "slots":
            frameworkElement = RenderNewControl(e); break;
        case "lb_table": case "lb_podium": case "lb_rank": case "lb_icon": case "lb_range": case "lb_slider": case "lb_me": case "lb_periods":
            frameworkElement = RenderBoardWidget(e); break;
        case "item":
            var icon=ItemImage(e.Item);frameworkElement=icon!=null?new Image {Source=icon,Stretch=Stretch.Uniform}:new TextBlock {Text=e.Item,Foreground=Brush(e.Foreground),TextWrapping=TextWrapping.Wrap};RenderOptions.SetBitmapScalingMode(frameworkElement,BitmapScalingMode.NearestNeighbor);break;
		default:
			frameworkElement = new TextBlock
			{
				Text = ((e.Type == "item") ? ("◇ " + e.Item) : e.Wrap ? WrappedText(e.Text) : e.Text), TextWrapping = e.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
				Foreground = Brush(e.Foreground),
				Background = ((e.Type == "label") ? Brushes.Transparent : Brush(e.Background)),
				VerticalAlignment = VerticalAlignment.Center,
				FontSize = Math.Clamp(e.FontScale * 14.0, 6.0, 96.0),
				TextAlignment = ((e.Alignment == "center") ? TextAlignment.Center : ((e.Alignment == "right") ? TextAlignment.Right : TextAlignment.Left))
			};
			break;
		}
		// On a leaderboard page a label shows what {title}, {players} and the like become (with sample figures).
		if (ui.IsLeaderboard && e.Type == "label" && frameworkElement is TextBlock boardLabel) boardLabel.Text = BoardText(e.Text, 0);
		ApplyFont(frameworkElement, e);
		if (e.Type != "button")
		{
			frameworkElement = DecorateControl(frameworkElement, e);
		}
		frameworkElement.IsHitTestVisible = interactive;
		frameworkElement.IsEnabled = e.Enabled;
		frameworkElement.ToolTip = e.Tooltip;
		return frameworkElement;
	}

}
