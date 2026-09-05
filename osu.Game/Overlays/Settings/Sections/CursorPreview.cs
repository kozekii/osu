// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace osu.Game.Overlays.Settings.Sections
{
    public partial class CursorPreview : CompositeDrawable
    {
        [Resolved]
        private SkinManager skins { get; set; }

        [Resolved]
        private OsuConfigManager config { get; set; }

        [Resolved(canBeNull: true)]
        private OverlayColourProvider colourProvider { get; set; }

        private Container previewBox;
        private Container trailContainer;
        private Container rippleContainer;
        private Container cursorScaleContainer;
        private Container cursorVisualContainer;
        private Sprite cursorSprite;
        private Sprite cursorMiddleSprite;
        private Container defaultCursorContainer;
        private OsuSpriteText hintText;

        private Bindable<float> cursorSize;
        private Vector2 lastPosition;
        private bool isPressed;
        private double lastTrailTime;
        private bool disjointTrail;
        private Texture trailTexture;

        private const int max_trail_parts = 64;
        private readonly List<TrailPartSprite> trailPool = new List<TrailPartSprite>();

        public CursorPreview()
        {
            RelativeSizeAxes = Axes.X;
            Height = 175;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            cursorSize = config.GetBindable<float>(OsuSetting.GameplayCursorSize);

            InternalChild = previewBox = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 10,
                BorderThickness = 2,
                BorderColour = colourProvider?.Background4 ?? Colour4.FromHex("#2d3042"),
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colourProvider?.Background6 ?? Colour4.FromHex("#0d0e14"),
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Colour4.White.Opacity(0.02f),
                    },
                    new OsuSpriteText
                    {
                        Text = "LIVE CURSOR PREVIEW",
                        Font = OsuFont.Torus.With(size: 11, weight: FontWeight.Bold),
                        Colour = colourProvider?.Colour1 ?? Colour4.FromHex("#ec6099"),
                        Margin = new MarginPadding(12),
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Depth = float.MinValue,
                    },
                    hintText = new OsuSpriteText
                    {
                        Text = "Move & click to test cursor and trail",
                        Font = OsuFont.Torus.With(size: 11, weight: FontWeight.SemiBold),
                        Colour = colourProvider?.Content2 ?? Colour4.FromHex("#707890"),
                        Margin = new MarginPadding(12),
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Depth = float.MinValue,
                    },
                    trailContainer = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                    rippleContainer = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                    cursorScaleContainer = new Container
                    {
                        AutoSizeAxes = Axes.Both,
                        Origin = Anchor.Centre,
                        Anchor = Anchor.TopLeft,
                        Child = cursorVisualContainer = new Container
                        {
                            AutoSizeAxes = Axes.Both,
                            Origin = Anchor.Centre,
                            Anchor = Anchor.Centre,
                            Children = new Drawable[]
                            {
                                cursorSprite = new Sprite
                                {
                                    Origin = Anchor.Centre,
                                    Anchor = Anchor.Centre,
                                },
                                cursorMiddleSprite = new Sprite
                                {
                                    Origin = Anchor.Centre,
                                    Anchor = Anchor.Centre,
                                },
                                defaultCursorContainer = new Container
                                {
                                    Size = new Vector2(32),
                                    Origin = Anchor.Centre,
                                    Anchor = Anchor.Centre,
                                    Children = new Drawable[]
                                    {
                                        new CircularContainer
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Masking = true,
                                            BorderThickness = 3,
                                            BorderColour = Color4.White,
                                            EdgeEffect = new EdgeEffectParameters
                                            {
                                                Type = EdgeEffectType.Shadow,
                                                Colour = Color4.Pink.Opacity(0.5f),
                                                Radius = 5,
                                            },
                                            Child = new Box
                                            {
                                                RelativeSizeAxes = Axes.Both,
                                                Alpha = 0,
                                                AlwaysPresent = true,
                                            }
                                        },
                                        new Circle
                                        {
                                            Origin = Anchor.Centre,
                                            Anchor = Anchor.Centre,
                                            Size = new Vector2(8),
                                            Colour = new Color4(34, 93, 204, 255),
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            for (int i = 0; i < max_trail_parts; i++)
            {
                var part = new TrailPartSprite();
                trailPool.Add(part);
                trailContainer.Add(part);
            }

            cursorSize.BindValueChanged(scale =>
            {
                cursorScaleContainer.Scale = new Vector2(scale.NewValue);
            }, true);

            skins.SourceChanged += reloadSkin;
            reloadSkin();
        }

        private void reloadSkin()
        {
            var skin = skins.CurrentCursorSkin.Value ?? skins.CurrentSkin.Value;

            var cTex = skins.GetTexture("cursor", default, default);
            var cmTex = skins.GetTexture("cursormiddle", default, default);
            trailTexture = skins.GetTexture("cursortrail", default, default) ?? skins.GetTexture("Cursor/cursortrail", default, default);

            cursorSprite.Texture = cTex;
            cursorMiddleSprite.Texture = cmTex;

            disjointTrail = cmTex == null;

            bool isLegacyCursor = cTex != null;
            defaultCursorContainer.Alpha = isLegacyCursor ? 0 : 1;
            cursorSprite.Alpha = isLegacyCursor ? 1 : 0;
            cursorMiddleSprite.Alpha = cmTex != null ? 1 : 0;

            if (cTex != null)
            {
                cursorSprite.ClearTransforms();
                cursorSprite.Spin(10000, RotationDirection.Clockwise);
            }

            foreach (var part in trailPool)
            {
                part.Texture = trailTexture;
                part.Blending = disjointTrail ? BlendingParameters.Inherit : BlendingParameters.Additive;
            }
        }

        protected override bool OnHover(HoverEvent e)
        {
            previewBox.BorderColour = colourProvider?.Colour1 ?? Colour4.FromHex("#ec6099");
            hintText.FadeColour(colourProvider?.Content1 ?? Colour4.White, 200);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            previewBox.BorderColour = colourProvider?.Background4 ?? Colour4.FromHex("#2d3042");
            hintText.FadeColour(colourProvider?.Content2 ?? Colour4.FromHex("#707890"), 200);
            base.OnHoverLost(e);
        }

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            Vector2 localPos = ToLocalSpace(e.ScreenSpaceMousePosition);
            moveCursorTo(localPos);
            return base.OnMouseMove(e);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button == MouseButton.Left || e.Button == MouseButton.Right)
            {
                isPressed = true;
                cursorVisualContainer.ScaleTo(1.3f, 100, Easing.Out);

                spawnRipple(cursorScaleContainer.Position);
                return true;
            }

            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            if (e.Button == MouseButton.Left || e.Button == MouseButton.Right)
            {
                isPressed = false;
                cursorVisualContainer.ScaleTo(1.0f, 100, Easing.Out);
            }

            base.OnMouseUp(e);
        }

        private void spawnRipple(Vector2 position)
        {
            var ripple = new CircularContainer
            {
                Origin = Anchor.Centre,
                Anchor = Anchor.TopLeft,
                Position = position,
                Size = new Vector2(20),
                Masking = true,
                BorderThickness = 2.5f,
                BorderColour = Color4.White,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0,
                    AlwaysPresent = true,
                }
            };

            rippleContainer.Add(ripple);
            ripple.ScaleTo(3.5f, 600, Easing.OutQuint)
                  .FadeOut(600, Easing.OutQuint)
                  .Expire();
        }

        private void moveCursorTo(Vector2 position)
        {
            cursorScaleContainer.Position = position;

            if (Time.Current - lastTrailTime >= (disjointTrail ? 16.6 : 8.0))
            {
                if (Vector2.DistanceSquared(lastPosition, position) > 2.0f)
                {
                    addTrailPoint(position);
                    lastPosition = position;
                    lastTrailTime = Time.Current;
                }
            }
        }

        private void addTrailPoint(Vector2 position)
        {
            foreach (var part in trailPool)
            {
                if (part.Alpha <= 0.05f)
                {
                    part.Position = position;
                    part.Scale = new Vector2(cursorSize.Value * (isPressed ? 1.3f : 1.0f));
                    part.Rotation = cursorSprite.Rotation;
                    part.FadeTo(0.8f)
                        .FadeOut(disjointTrail ? 180 : 350, Easing.Out);
                    break;
                }
            }
        }

        protected override void Update()
        {
            base.Update();

            if (!IsHovered)
            {
                double t = Time.Current / 700.0;
                float halfW = DrawWidth * 0.5f;
                float halfH = DrawHeight * 0.5f;
                float x = halfW + MathF.Sin((float)t * 1.5f) * (halfW * 0.7f);
                float y = halfH + MathF.Sin((float)t * 3.0f) * (halfH * 0.45f);

                moveCursorTo(new Vector2(x, y));
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (skins != null)
                skins.SourceChanged -= reloadSkin;
        }

        private partial class TrailPartSprite : Sprite
        {
            public TrailPartSprite()
            {
                Origin = Anchor.Centre;
                Anchor = Anchor.TopLeft;
                Alpha = 0;
            }
        }
    }
}
