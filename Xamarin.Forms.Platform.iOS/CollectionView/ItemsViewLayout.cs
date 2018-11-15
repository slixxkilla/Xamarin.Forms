using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Xamarin.Forms.Platform.iOS
{
	public abstract class ItemsViewLayout : UICollectionViewFlowLayout, IUICollectionViewDelegateFlowLayout
	{
		readonly ItemsLayout _itemsLayout;
		bool _determiningCellSize;
		bool _disposed;

		protected ItemsViewLayout(ItemsLayout itemsLayout)
		{
			Xamarin.Forms.CollectionView.VerifyCollectionViewFlagEnabled(nameof(ItemsViewLayout));

			_itemsLayout = itemsLayout;
			_itemsLayout.PropertyChanged += LayoutOnPropertyChanged;

			var scrollDirection = itemsLayout.Orientation == ItemsLayoutOrientation.Horizontal
				? UICollectionViewScrollDirection.Horizontal
				: UICollectionViewScrollDirection.Vertical;

			Initialize(scrollDirection);
		}

		protected override void Dispose(bool disposing)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;

			if (disposing)
			{
				if (_itemsLayout != null)
				{
					_itemsLayout.PropertyChanged += LayoutOnPropertyChanged;
				}
			}

			base.Dispose(disposing);
		}

		void LayoutOnPropertyChanged(object sender, PropertyChangedEventArgs propertyChanged)
		{
			HandlePropertyChanged(propertyChanged);
		}

		protected virtual void HandlePropertyChanged(PropertyChangedEventArgs  propertyChanged)
		{
			// Nothing to do here for now; may need something here when we implement Snapping
		}

		public nfloat ConstrainedDimension { get; set; }

		public Func<UICollectionViewCell> GetPrototype { get; set; }

		// TODO hartez 2018/09/14 17:24:22 Long term, this needs to use the ItemSizingStrategy enum and not be locked into bool	
		public bool UniformSize { get; set; }

		public abstract void ConstrainTo(CGSize size);

		[Export("collectionView:layout:insetForSectionAtIndex:")]
		[CompilerGenerated]
		public virtual UIEdgeInsets GetInsetForSection(UICollectionView collectionView, UICollectionViewLayout layout,
			nint section)
		{
			return UIEdgeInsets.Zero;
		}

		[Export("collectionView:layout:minimumInteritemSpacingForSectionAtIndex:")]
		[CompilerGenerated]
		public virtual nfloat GetMinimumInteritemSpacingForSection(UICollectionView collectionView,
			UICollectionViewLayout layout, nint section)
		{
			return (nfloat)0.0;
		}

		[Export("collectionView:layout:minimumLineSpacingForSectionAtIndex:")]
		[CompilerGenerated]
		public virtual nfloat GetMinimumLineSpacingForSection(UICollectionView collectionView,
			UICollectionViewLayout layout, nint section)
		{
			return (nfloat)0.0;
		}

		public void PrepareCellForLayout(ItemsViewCell cell)
		{
			if (_determiningCellSize)
			{
				return;
			}

			if (EstimatedItemSize == CGSize.Empty)
			{
				cell.ConstrainTo(ItemSize);
			}
			else
			{
				cell.ConstrainTo(ConstrainedDimension);
			}
		}

		public override bool ShouldInvalidateLayoutForBoundsChange(CGRect newBounds)
		{
			var shouldInvalidate = base.ShouldInvalidateLayoutForBoundsChange(newBounds);

			if (shouldInvalidate)
			{
				UpdateConstraints(newBounds.Size);
			}

			return shouldInvalidate;
		}

		protected void DetermineCellSize()
		{
			if (GetPrototype == null)
			{
				return;
			}

			_determiningCellSize = true;

			if (!Forms.IsiOS10OrNewer)
			{
				// iOS 9 will throw an exception during auto layout if no EstimatedSize is set
				EstimatedItemSize = new CGSize(1, 1);
			}

			if (!(GetPrototype() is ItemsViewCell prototype))
			{
				return;
			}

			prototype.ConstrainTo(ConstrainedDimension);

			var measure = prototype.Measure();

			if (UniformSize)
			{
				ItemSize = measure;

				// Make sure autolayout is disabled 
				EstimatedItemSize = CGSize.Empty;
			}
			else
			{
				EstimatedItemSize = measure;
			}

			_determiningCellSize = false;
		}

		bool ConstraintsMatchScrollDirection(CGSize size)
		{
			if (ScrollDirection == UICollectionViewScrollDirection.Vertical)
			{
				return ConstrainedDimension == size.Width;
			}

			return ConstrainedDimension == size.Height;
		}

		void Initialize(UICollectionViewScrollDirection scrollDirection)
		{
			ScrollDirection = scrollDirection;
		}

		void UpdateCellConstraints()
		{
			var cells = CollectionView.VisibleCells;

			for (int n = 0; n < cells.Length; n++)
			{
				if (cells[n] is ItemsViewCell constrainedCell)
				{
					PrepareCellForLayout(constrainedCell);
				}
			}
		}

		void UpdateConstraints(CGSize size)
		{
			if (ConstraintsMatchScrollDirection(size))
			{
				return;
			}

			ConstrainTo(size);
			UpdateCellConstraints();
		}

		public override CGPoint TargetContentOffset(CGPoint proposedContentOffset, CGPoint scrollingVelocity)
		{
						var snapPointsType = _itemsLayout.SnapPointsType;

			if (snapPointsType == SnapPointsType.None)
			{
				// Nothing to do here; fall back to the default
				return base.TargetContentOffset(proposedContentOffset, scrollingVelocity);
			}

			// Get the viewport of the UICollectionView
			var viewport = new CGRect(proposedContentOffset, CollectionView.Bounds.Size);

			// And find all the elements currently visible in the viewport
			var visibleElements = LayoutAttributesForElementsInRect(viewport);

			if (visibleElements.Length == 0)
			{
				// Nothing to see here; fall back to the default
				return base.TargetContentOffset(proposedContentOffset, scrollingVelocity);
			}

			var alignment = _itemsLayout.SnapPointsAlignment;

			if (visibleElements.Length == 1)
			{
				// If there is only one item in the viewport and snapping is mandatory,
				// then we need to align the viewport with it
				if (snapPointsType == SnapPointsType.Mandatory || snapPointsType == SnapPointsType.MandatorySingle)
				{
					return SnapHelpers.AdjustContentOffset(proposedContentOffset, visibleElements[0].Frame, viewport,
						alignment, ScrollDirection);
				}
			}

			// If there are multiple items in the viewport, we need to choose the one which is 
			// closest to the relevant part of the viewport while being sufficiently visible

			// Find the spot in the viewport we're trying to align with
			var alignmentTarget = SnapHelpers.FindAlignmentTarget(alignment, proposedContentOffset, 
				CollectionView, ScrollDirection);

			// Find the closest sufficiently visible candidate
			var bestCandidate = SnapHelpers.FindBestSnapCandidate(visibleElements, viewport, alignmentTarget);
			
			if (bestCandidate != null)
			{
				return SnapHelpers.AdjustContentOffset(proposedContentOffset, bestCandidate.Frame, viewport, alignment,
					ScrollDirection);
			}

			// If we got this far an nothing matched, it means that we have multiple items but somehow
			// none of them fit at least half in the viewport. So just fall back to the first item, if 
			// snapping is mandatory
			if (snapPointsType == SnapPointsType.Mandatory || snapPointsType == SnapPointsType.MandatorySingle)
			{
				return SnapHelpers.AdjustContentOffset(proposedContentOffset, visibleElements[0].Frame, viewport, alignment,
					ScrollDirection);
			}

			return base.TargetContentOffset(proposedContentOffset, scrollingVelocity);
		}
	}
}
