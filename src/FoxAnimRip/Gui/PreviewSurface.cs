// SPDX-License-Identifier: MIT
using System.ComponentModel;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using FoxAnimRip.Preview;

namespace FoxAnimRip.Gui;

/// <summary>
/// The 3D view: a bitmap the renderer draws into, plus the mouse handling
/// everyone already knows from every other model viewer.
///
/// Left drag orbits, right or middle drag pans, the wheel zooms, double-click
/// re-frames. There is nothing clever here -- the point of the control is that
/// nobody has to think about it.
/// </summary>
public sealed class PreviewSurface : Control
{
    private readonly PreviewRenderer _renderer = new();
    private Bitmap _bitmap;
    private Point _drag;
    private MouseButtons _dragging = MouseButtons.None;

    // This control is built in code and never dropped on a designer surface,
    // so nothing here should be serialised into a .Designer.cs.
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PreviewModel Model { get; private set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PreviewClip Clip { get; private set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Frame { get; set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PreviewOptions Options => _renderer.Options;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public OrbitCamera Camera => _renderer.Camera;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double LastFrameMs => _renderer.LastFrameMs;

    public PreviewSurface()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = false;      // we present a finished bitmap ourselves
        BackColor = Color.FromArgb(26, 28, 33);
        TabStop = true;
    }

    public void SetModel(PreviewModel model)
    {
        Model = model;
        Clip = null;
        Frame = 0;
        ResetView();
        Invalidate();
    }

    public void SetClip(PreviewClip clip)
    {
        Clip = clip;
        Frame = 0;
        Invalidate();
    }

    public void ResetView()
    {
        if (Model is null) return;
        Camera.Yaw = 0.55f;
        Camera.Pitch = 0.12f;
        Camera.Frame(Model.Centre, Model.Radius);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var width = Math.Max(1, ClientSize.Width);
        var height = Math.Max(1, ClientSize.Height);
        _renderer.Resize(width, height);
        _renderer.Render(Model, Clip, Frame);

        if (_bitmap is null || _bitmap.Width != width || _bitmap.Height != height)
        {
            _bitmap?.Dispose();
            _bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        }

        var data = _bitmap.LockBits(new Rectangle(0, 0, width, height),
                                    ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            var pixels = _renderer.Pixels;
            // Stride can exceed width on some formats; copy row by row so a
            // padded bitmap does not come out sheared.
            if (data.Stride == width * 4)
            {
                Marshal.Copy(pixels, 0, data.Scan0, width * height);
            }
            else
            {
                for (var y = 0; y < height; y++)
                    Marshal.Copy(pixels, y * width,
                                 data.Scan0 + y * data.Stride, width);
            }
        }
        finally { _bitmap.UnlockBits(data); }

        e.Graphics.DrawImageUnscaled(_bitmap, 0, 0);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { /* fully covered */ }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        _drag = e.Location;
        _dragging = e.Button;
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = MouseButtons.None;
        base.OnMouseUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging != MouseButtons.None)
        {
            var dx = e.X - _drag.X;
            var dy = e.Y - _drag.Y;
            _drag = e.Location;

            if (_dragging == MouseButtons.Left)
                Camera.Orbit(dx * -0.01f, dy * 0.01f);
            else
                Camera.Pan(dx / (float)Math.Max(1, Width), dy / (float)Math.Max(1, Height));
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Camera.Zoom(e.Delta > 0 ? 0.88f : 1f / 0.88f);
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        ResetView();
        base.OnMouseDoubleClick(e);
    }

    protected override bool IsInputKey(Keys key) => key switch
    {
        Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Space => true,
        _ => base.IsInputKey(key),
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bitmap?.Dispose();
        base.Dispose(disposing);
    }
}
