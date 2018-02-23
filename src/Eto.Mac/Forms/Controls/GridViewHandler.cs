using System;
using Eto.Forms;
using System.Collections.Generic;
using Eto.Drawing;
using System.Collections;
using System.Linq;
using Eto.Mac.Forms.Cells;

#if XAMMAC2
using AppKit;
using Foundation;
using CoreGraphics;
using ObjCRuntime;
using CoreAnimation;
#else
using MonoMac.AppKit;
using MonoMac.Foundation;
using MonoMac.CoreGraphics;
using MonoMac.ObjCRuntime;
using MonoMac.CoreAnimation;
#if Mac64
using nfloat = System.Double;
using nint = System.Int64;
using nuint = System.UInt64;
#else
using nfloat = System.Single;
using nint = System.Int32;
using nuint = System.UInt32;
#endif
#if SDCOMPAT
using CGSize = System.Drawing.SizeF;
using CGRect = System.Drawing.RectangleF;
using CGPoint = System.Drawing.PointF;
#endif
#endif

namespace Eto.Mac.Forms.Controls
{
	public class GridViewHandler : GridHandler<GridViewHandler.EtoTableView, GridView, GridView.ICallback>, GridView.IHandler
	{
		CollectionHandler collection;

		public class EtoTableView : NSTableView, IMacControl
		{
			public WeakReference WeakHandler { get; set; }

			public GridViewHandler Handler
			{
				get { return (GridViewHandler)WeakHandler.Target; }
				set { WeakHandler = new WeakReference(value); }
			}

			/// <summary>
			/// The area to the right and below the rows is not filled with the background
			/// color. This fixes that. See http://orangejuiceliberationfront.com/themeing-nstableview/
			/// </summary>
			public override void DrawBackground(CGRect clipRect)
			{
				var backgroundColor = Handler.BackgroundColor;
				if (backgroundColor != Colors.Transparent)
				{
					backgroundColor.ToNSUI().Set();
					NSGraphics.RectFill(clipRect);
				}
				else
					base.DrawBackground(clipRect);
			}

			public override void RightMouseDown(NSEvent theEvent)
			{
				if (HandleMouseEvent(theEvent))
					return;
				base.RightMouseDown(theEvent);
			}

			public override void OtherMouseDown(NSEvent theEvent)
			{
				if (HandleMouseEvent(theEvent))
					return;
				base.OtherMouseDown(theEvent);
			}

			public override void MouseDown(NSEvent theEvent)
			{
				if (HandleMouseEvent(theEvent))
					return;

				Handler.IsMouseDragging = true;
				base.MouseDown(theEvent);
				Handler.IsMouseDragging = true;
			}

			bool HandleMouseEvent(NSEvent theEvent)
			{
				var handler = Handler;
				if (handler != null)
				{
					var args = MacConversions.GetMouseEvent(handler.ContainerControl, theEvent, false);
					if (theEvent.ClickCount >= 2)
						handler.Callback.OnMouseDoubleClick(handler.Widget, args);
					else
						handler.Callback.OnMouseDown(handler.Widget, args);
					if (args.Handled)
						return true;

					var point = ConvertPointFromView(theEvent.LocationInWindow, null);

					int rowIndex;
					if ((rowIndex = (int)GetRow(point)) >= 0)
					{
						int columnIndex = (int)GetColumn(point);
						var item = handler.GetItem(rowIndex);
						var column = columnIndex == -1 || columnIndex > handler.Widget.Columns.Count ? null : handler.Widget.Columns[columnIndex];
						var cellArgs = MacConversions.CreateCellMouseEventArgs(column, handler.ContainerControl, rowIndex, columnIndex, item, theEvent);
						if (theEvent.ClickCount >= 2)
							handler.Callback.OnCellDoubleClick(handler.Widget, cellArgs);
						else
							handler.Callback.OnCellClick(handler.Widget, cellArgs);
					}
				}
				return false;
			}

			public EtoTableView(GridViewHandler handler)
			{
				FocusRingType = NSFocusRingType.None;
				DataSource = new EtoTableViewDataSource { Handler = handler };
				Delegate = new EtoTableDelegate { Handler = handler };
				ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.None;
				SetDraggingSourceOperationMask(NSDragOperation.All, true);
				SetDraggingSourceOperationMask(NSDragOperation.All, false);
			}

			public NSDragOperation? AllowedOperation { get; set; }

			[Export("draggingSession:sourceOperationMaskForDraggingContext:")]
			public NSDragOperation DraggingSessionSourceOperationMask(NSDraggingSession session, IntPtr context)
			{
				return AllowedOperation ?? NSDragOperation.None;
			}

			public override void WillOpenMenu(NSMenu menu, NSEvent theEvent)
			{
				if (!Handler.SelectedRows.Contains((int)ClickedRow))
					Handler.CustomSelectedRows = new[] { (int)ClickedRow };
				base.WillOpenMenu(menu, theEvent);
			}

			public override void DidCloseMenu(NSMenu menu, NSEvent theEvent)
			{
				base.DidCloseMenu(menu, theEvent);
				// action is called after this, so we can't set selected rows direclty
				Application.Instance.AsyncInvoke(() => Handler.CustomSelectedRows = null);
			}
		}

		public class EtoTableViewDataSource : NSTableViewDataSource
		{
			WeakReference handler;

			public GridViewHandler Handler { get { return (GridViewHandler)(handler != null ? handler.Target : null); } set { handler = new WeakReference(value); } }

			public override nint GetRowCount(NSTableView tableView)
			{
				return (Handler.collection != null && Handler.collection.Collection != null) ? Handler.collection.Count : 0;
			}

			public override NSObject GetObjectValue(NSTableView tableView, NSTableColumn tableColumn, nint row)
			{
				var item = Handler.collection.ElementAt((int)row);
				var colHandler = Handler.GetColumn(tableColumn);
				return colHandler == null ? null : colHandler.GetObjectValue(item);
			}

			public override void SetObjectValue(NSTableView tableView, NSObject theObject, NSTableColumn tableColumn, nint row)
			{
				var item = Handler.collection.ElementAt((int)row);
				var colHandler = Handler.GetColumn(tableColumn);
				if (colHandler != null && Handler.SuppressUpdate == 0)
				{
					colHandler.SetObjectValue(item, theObject);

					Handler.SetIsEditing(false);
					Handler.Callback.OnCellEdited(Handler.Widget, new GridViewCellEventArgs(colHandler.Widget, (int)row, colHandler.Column, item));
				}
			}

			public override NSDragOperation ValidateDrop(NSTableView tableView, NSDraggingInfo info, nint row, NSTableViewDropOperation dropOperation)
			{
				var h = Handler;
				if (h == null)
					return NSDragOperation.None;
				var etoInfo = GetDragInfo(info, row, dropOperation);
				var e = h.GetDragEventArgs(info, etoInfo);
				h.Callback.OnDragOver(h.Widget, e);
				if (e.AllowedEffects.HasFlag(e.Effects))
				{
					var idx = etoInfo.Index;
					if (idx >= 0)
					{
						var pos = etoInfo.Position;
						if (pos == GridDragPosition.After)
						{
							pos = GridDragPosition.Before;
							idx++;
						}
						tableView.SetDropRowDropOperation((nint)idx, pos == GridDragPosition.Over ? NSTableViewDropOperation.On : NSTableViewDropOperation.Above);
					}
					else
					{
						tableView.SetDropRowDropOperation(-1, NSTableViewDropOperation.On);
					}

					return e.Effects.ToNS();
				}
				else
					return NSDragOperation.None;
			}

			GridViewDragInfo GetDragInfo(NSDraggingInfo info, nint row, NSTableViewDropOperation dropOperation)
			{
				var h = Handler;
				var tableView = h.Control;
				var position = GridDragPosition.Over;
				if (dropOperation == NSTableViewDropOperation.On)
					position = GridDragPosition.Over;
				else
				{
					// need to check if it's actually above or below the item under the cursor
					var rowUnderMouse = tableView.GetRow(tableView.ConvertPointFromView(info.DraggingLocation, null));
					if (row == rowUnderMouse)
					{
						position = GridDragPosition.Before;
					}
					else
					{
						position = GridDragPosition.After;
						if (row > 0)
							row--;
					}
				}
				var item = row >= 0 ? h.GetItem((int)row) : null;

				return new GridViewDragInfo(h.Widget, item, (int)row, position);
			}

			public override bool AcceptDrop(NSTableView tableView, NSDraggingInfo info, nint row, NSTableViewDropOperation dropOperation)
			{
				var h = Handler;
				if (h == null)
					return false;
				var e = h.GetDragEventArgs(info, GetDragInfo(info, row, dropOperation));
				h.Callback.OnDragLeave(h.Widget, e);
				h.Callback.OnDragDrop(h.Widget, e);
				return true;
			}

			public override bool WriteRows(NSTableView tableView, NSIndexSet rowIndexes, NSPasteboard pboard)
			{
				var h = Handler;
				if (h == null)
					return false;

				if (h.IsMouseDragging)
				{
					h.Control.AllowedOperation = null;
					// give MouseMove event a chance to start the drag
					h.DragPasteboard = pboard;
					h.CustomSelectedRows = rowIndexes.Select(r => (int)r).ToList();
					var args = MacConversions.GetMouseEvent(h.ContainerControl, NSApplication.SharedApplication.CurrentEvent, false);
					h.Callback.OnMouseMove(h.Widget, args);
					h.DragPasteboard = null;
					h.CustomSelectedRows = null;
					return h.Control.AllowedOperation != null;
				}

				return false;
			}
		}

		public class EtoTableDelegate : NSTableViewDelegate
		{
			WeakReference handler;

			public GridViewHandler Handler { get { return (GridViewHandler)(handler != null ? handler.Target : null); } set { handler = new WeakReference(value); } }

			public override bool ShouldEditTableColumn(NSTableView tableView, NSTableColumn tableColumn, nint row)
			{
				var colHandler = Handler.GetColumn(tableColumn);
				var item = Handler.collection.ElementAt((int)row);
				var args = new GridViewCellEventArgs(colHandler.Widget, (int)row, colHandler.Column, item);
				Handler.Callback.OnCellEditing(Handler.Widget, args);
				Handler.SetIsEditing(true);
				return true;
			}

			NSIndexSet previouslySelected;
			public override void SelectionDidChange(NSNotification notification)
			{
				if (Handler.SuppressSelectionChanged == 0)
				{
					Handler.Callback.OnSelectionChanged(Handler.Widget, EventArgs.Empty);
					var columns = NSIndexSet.FromNSRange(new NSRange(0, Handler.Control.TableColumns().Length));
					if (previouslySelected != null)
						Handler.Control.ReloadData(previouslySelected, columns);
					var selected = Handler.Control.SelectedRows;
					Handler.Control.ReloadData(selected, columns);
					previouslySelected = selected;
				}
			}

			public override nfloat GetSizeToFitColumnWidth(NSTableView tableView, nint column)
			{
				var colHandler = Handler.GetColumn(tableView.TableColumns()[column]);
				if (colHandler != null)
				{
					// turn on autosizing for this column again
					Application.Instance.AsyncInvoke(() => colHandler.AutoSize = true);
					return colHandler.GetPreferredWidth();
				}
				return 20;
			}

			public override void DidClickTableColumn(NSTableView tableView, NSTableColumn tableColumn)
			{
				var colHandler = Handler.GetColumn(tableColumn);
				if (colHandler.Sortable)
					Handler.Callback.OnColumnHeaderClick(Handler.Widget, new GridColumnEventArgs(colHandler.Widget));
			}

			public override void ColumnDidResize(NSNotification notification)
			{
				if (!Handler.IsAutoSizingColumns)
				{
					// when the user resizes the column, don't autosize anymore when data/scroll changes
					var column = notification.UserInfo["NSTableColumn"] as NSTableColumn;
					if (column != null)
					{
						var colHandler = Handler.GetColumn(column);
						colHandler.AutoSize = false;
					}
				}
			}

			public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
			{
				var colHandler = Handler.GetColumn(tableColumn);
				if (colHandler != null)
				{
					var cellHandler = colHandler.DataCellHandler;
					if (cellHandler != null)
					{
						return cellHandler.GetViewForItem(tableView, tableColumn, (int)row, null, (obj, r) => Handler.GetItem(r));
					}
				}

				return tableView.MakeView(tableColumn.Identifier, this);
			}
		}

		public override void AttachEvent(string id)
		{
			switch (id)
			{
				case Grid.CellEditingEvent:
					// handled by delegate
					/* following should work, but internal delegate to trigger event does not work
					table.ShouldEditTableColumn = (tableView, tableColumn, row) => {
						var id = tableColumn.Identifier as EtoGridColumnIdentifier;
						var item = store.GetItem (row);
						var args = new GridViewCellArgs(id.Handler.Widget, row, id.Handler.Column, item);
						this.Widget.OnBeginCellEdit (args);
						return true;
					};*/
					break;
				case Grid.CellEditedEvent:
					// handled after object value is set
					break;
				case Grid.SelectionChangedEvent:
					/* handled by delegate, for now
					table.SelectionDidChange += delegate {
						Widget.OnSelectionChanged (EventArgs.Empty);
					};*/
					break;
				case Grid.ColumnHeaderClickEvent:
					/*
					table.DidClickTableColumn += delegate(object sender, NSTableViewTableEventArgs e) {
						var column = Handler.Widget.Columns.First (r => object.ReferenceEquals (r.ControlObject, tableColumn));
						Handler.Widget.OnHeaderClick (new GridColumnEventArgs (column));
					};
					*/
					break;
				case Grid.CellDoubleClickEvent:
				case Grid.CellClickEvent:
					// Handled in EtoTableView
					break;
				case Grid.CellFormattingEvent:
					break;
				case Eto.Forms.Control.DragOverEvent:
				case Eto.Forms.Control.DragDropEvent:
					// handled in EtoTableViewDataSource
					break;
				default:
					base.AttachEvent(id);
					break;
			}
		}

		protected override EtoTableView CreateControl()
		{
			return new EtoTableView(this);
		}

		public IEnumerable<object> SelectedItems
		{
			get
			{
				if (collection != null)
				{
					foreach (var row in SelectedRows)
						yield return collection.ElementAt(row);
				}
			}
		}

		static readonly object CustomSelectedRows_Key = new object();

		protected IEnumerable<int> CustomSelectedRows
		{
			get { return Widget.Properties.Get<IEnumerable<int>>(CustomSelectedRows_Key); }
			set { Widget.Properties.Set(CustomSelectedRows_Key, value); }
		}


		public override IEnumerable<int> SelectedRows
		{
			get { return CustomSelectedRows ?? base.SelectedRows; }
			set { base.SelectedRows = value; }
		}

		class CollectionHandler : EnumerableChangedHandler<object>
		{
			public GridViewHandler Handler { get; set; }

			public override void AddRange(IEnumerable<object> items)
			{
				Handler.Control.ReloadData();
				Handler.AutoSizeColumns(true);
			}

			static Selector selInsertRowsWithAnimation = new Selector("insertRowsAtIndexes:withAnimation:");
			static Selector selRemoveRowsWithAnimation = new Selector("removeRowsAtIndexes:withAnimation:");

			public override void AddItem(object item)
			{
				if (Handler.Control.RespondsToSelector(selInsertRowsWithAnimation))
				{
					Handler.Control.BeginUpdates();
					Handler.Control.InsertRows(new NSIndexSet(Count), NSTableViewAnimation.SlideDown);
					Handler.Control.EndUpdates();
				}
				else
					Handler.Control.ReloadData();

				Handler.AutoSizeColumns(true);
			}

			public override void InsertItem(int index, object item)
			{
				if (Handler.Control.RespondsToSelector(selInsertRowsWithAnimation))
				{
					Handler.Control.BeginUpdates();
					Handler.Control.InsertRows(new NSIndexSet(index), NSTableViewAnimation.SlideDown);
					Handler.Control.EndUpdates();
				}
				else
				{
					var rows = Handler.SelectedRows.Select(r => r >= index ? r + 1 : r).ToArray();
					Handler.SuppressSelectionChanged++;
					Handler.Control.ReloadData();
					Handler.SelectedRows = rows;
					Handler.SuppressSelectionChanged--;
				}

				Handler.AutoSizeColumns(true);
			}

			public override void RemoveItem(int index)
			{
				if (Handler.Control.RespondsToSelector(selRemoveRowsWithAnimation))
				{
					Handler.Control.BeginUpdates();
					Handler.Control.RemoveRows(new NSIndexSet(index), NSTableViewAnimation.SlideUp);
					Handler.Control.EndUpdates();
				}
				else
				{
					// need to adjust selected rows to shift them up
					bool isSelected = false;
					var rows = Handler.SelectedRows.Where(r =>
					{
						if (r != index)
							return true;
						isSelected = true;
						return false;
					}).Select(r => r > index ? r - 1 : r).ToArray();
					Handler.SuppressSelectionChanged++;
					Handler.Control.ReloadData();
					Handler.SelectedRows = rows;
					Handler.SuppressSelectionChanged--;
					// item being removed was selected, so trigger change
					if (isSelected)
						Handler.Callback.OnSelectionChanged(Handler.Widget, EventArgs.Empty);
				}

				Handler.AutoSizeColumns(true);
			}

			public override void RemoveAllItems()
			{
				Handler.Control.ReloadData();
				Handler.AutoSizeColumns(true);
			}
		}

		public IEnumerable<object> DataStore
		{
			get { return collection != null ? collection.Collection : null; }
			set
			{
				if (collection != null)
					collection.Unregister();
				collection = new CollectionHandler { Handler = this };
				collection.Register(value);
				if (Widget.Loaded)
					AutoSizeColumns(true);
			}
		}

		public override object GetItem(int row)
		{
			if (collection == null || row >= collection.Count)
				return null;
			return collection.ElementAt(row);
		}

		public void ReloadData(IEnumerable<int> rows)
		{
			Control.ReloadData(NSIndexSet.FromArray(rows.Select(r => (nuint)r).ToArray()), NSIndexSet.FromNSRange(new NSRange(0, Control.TableColumns().Length)));
		}

		public object GetCellAt(PointF location, out int column, out int row)
		{
			location += ScrollView.ContentView.Bounds.Location.ToEto();
			var nslocation = location.ToNS();
			column = (int)Control.GetColumn(nslocation);
			row = (int)Control.GetRow(nslocation);
			return row >= 0 ? GetItem(row) : null;
		}

		static readonly object DragPasteboard_Key = new object();

		NSPasteboard DragPasteboard
		{
			get { return Widget.Properties.Get<NSPasteboard>(DragPasteboard_Key); }
			set { Widget.Properties.Set(DragPasteboard_Key, value); }
		}

		public override void DoDragDrop(DataObject data, DragEffects allowedAction)
		{
			if (DragPasteboard != null)
			{
				var handler = data.Handler as IDataObjectHandler;
				handler?.Apply(DragPasteboard);
				Control.AllowedOperation = allowedAction.ToNS();
			}
			else
			{
				base.DoDragDrop(data, allowedAction);
			}
		}

		public GridViewDragInfo GetDragInfo(DragEventArgs args) => args.ControlObject as GridViewDragInfo;
	}
}

