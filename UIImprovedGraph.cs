﻿using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Globalization;
using ColossalFramework.UI;
using UnityEngine;

namespace MoreCityStatistics
{
    /// <summary>
    /// improved UIGraph
    /// </summary>
    public class UIImprovedGraph : UISprite
    {
        /// <summary>
        /// settings for a curve on the graph
        /// </summary>
        private class CurveSettings
        {
            // the settings for one curve
            private string _description;
            private string _units;
            private string _numberFormat;
            private double?[] _data;
            private float _width;
            private Color32 _color;

            // values computed from the data
            private double _minValue;
            private double _maxValue;

            /// <summary>
            /// construct a curve setting
            /// </summary>
            public CurveSettings(string description, string units, string numberFormat, double?[] data, float width, Color32 color)
            {
                // check parameters
                if (string.IsNullOrEmpty(description))  { throw new ArgumentNullException("description");   }
                if (string.IsNullOrEmpty(units))        { throw new ArgumentNullException("units");         }
                if (string.IsNullOrEmpty(numberFormat)) { throw new ArgumentNullException("numberFormat");  }
                if (data == null)                       { throw new ArgumentNullException("data");          }
                if (data.Length == 0)                   { throw new ArgumentOutOfRangeException("data");    }
                if (width <= float.Epsilon)             { throw new ArgumentOutOfRangeException("width");   }

                // save parameters
                _description = description;
                _units = units;
                _numberFormat = numberFormat;
                _data = data;
                _width = width;
                _color = color;

                // compute min and max of data points that have a value
                bool hasValue = false;
                _minValue = double.MaxValue;
                _maxValue = double.MinValue;
                foreach (double? dataPoint in _data)
                {
                    if (dataPoint.HasValue)
                    {
                        double dataValue = dataPoint.Value;
                        _minValue = Math.Min(_minValue, dataValue);
                        _maxValue = Math.Max(_maxValue, dataValue);
                        hasValue = true;
                    }
                }

                // if curve has no value, then use 0 and 1
                if (!hasValue)
                {
                    _minValue = 0f;
                    _maxValue = 1f;
                }
            }

            // readonly accessors for the settings
            public string Description   { get { return _description;    } }
            public string Units         { get { return _units;          } }
            public string NumberFormat  { get { return _numberFormat;   } }
            public double?[] Data       { get { return _data;           } }
            public float Width          { get { return _width;          } }
            public Color32 Color        { get { return _color;          } }

            // readonly accessors for the computed values
            public double MinValue      { get { return _minValue;       } }
            public double MaxValue      { get { return _maxValue;       } }
        }

        // values that control the look of the graph
        private Color32 _textColor = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
        private Color32 _axesColor = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
        private Color32 _helpAxesColor = new Color32(128, 128, 128, byte.MaxValue);
        private float _axesWidth = 1f;
        private float _helpAxesWidth = 0.6f;    // thick enough so lines are still drawn at low resolutions
        private Rect _graphRect = new Rect(0.1f, 0.1f, 0.8f, 0.8f);
        private UIFont _font;

        // accessors that invalidate the graph whenever changed
        public Color32 TextColor     { get { return _textColor;     } set { _textColor     = value; Invalidate(); } }
        public Color32 AxesColor     { get { return _axesColor;     } set { _axesColor     = value; Invalidate(); } }
        public Color32 HelpAxesColor { get { return _helpAxesColor; } set { _helpAxesColor = value; Invalidate(); } }
        public float AxesWidth       { get { return _axesWidth;     } set { _axesWidth     = value; Invalidate(); } }
        public float HelpAxesWidth   { get { return _helpAxesWidth; } set { _helpAxesWidth = value; Invalidate(); } }
        public Rect GraphRect        { get { return _graphRect;     } set { _graphRect     = value; Invalidate(); } }
        public UIFont Font           { get { return _font;          } set { _font          = value; Invalidate(); } }

        // the dates for the horizontal axis
        private DateTime[] _dates;

        // values computed from the dates that define how to display the horizontal axis
        private int _startYear;
        private int _endYear;
        private int _incrementYear;
        private DateTime _startDate;
        private DateTime _endDate;
        private long _startTicks;
        private long _endTicks;
        private long _graphTickRange;

        // how to increment the date on the horizontal axis
        private enum DateIncrement
        {
            Years,
            Months6,
            Months3,
            Months2,
            Months1,
            Days10,
            Days5,
            Days2
        }
        DateIncrement _dateIncrement;

        // date constants (without time component)
        private static readonly DateTime MaxDate = DateTime.MaxValue.Date;
        private static readonly DateTime MinDate = DateTime.MinValue.Date;

        // the curves to be shown on the graph
        private List<CurveSettings> _curves = new List<CurveSettings>();

        // values computed from the curves that define how to display the vertical axis
        private double _minCurveValue;
        private double _maxCurveValue;
        private double _startValue;
        private double _endValue;
        private double _incrementValue;
        private double _graphValueRange;

        /// <summary>
        /// make sure font is initialized
        /// </summary>
        public override void Start()
        {
            // do base processing
            base.Start();

            // initialize font
            bool flag = _font != null && _font.isValid;
            if (Application.isPlaying && !flag)
            {
                _font = GetUIView().defaultFont;
            }
        }

        /// <summary>
        /// get the text render data
        /// </summary>
        private UIRenderData textRenderData
        {
            get
            {
                // if there are not 2, then add one
                while (m_RenderData.Count <= 1)
                {
                    UIRenderData item = UIRenderData.Obtain();
                    m_RenderData.Add(item);
                }

                // return the one that was added previously
                return m_RenderData[1];
            }
        }

        /// <summary>
        /// set the dates for the horizontal axis
        /// </summary>
        public void SetDates(DateTime[] dates)
        {
            // save the dates
            _dates = dates;

            // get first and last dates from the data
            DateTime firstDataDate;
            DateTime lastDataDate;
            if (_dates == null || _dates.Length == 0)
            {
                // use current game date
                firstDataDate = lastDataDate = SimulationManager.instance.m_currentGameTime.Date;
            }
            else
            {
                // verify dates are in ascending order with no duplicates
                DateTime date = _dates[0];
                for (int i = 1; i < _dates.Length; i++)
                {
                    if (_dates[i] <= date)
                    {
                        throw new InvalidOperationException("Dates must be in ascending order with no duplicates.");
                    }
                    date = _dates[i];
                }

                // get first and last dates
                firstDataDate = _dates[0];
                lastDataDate = _dates[_dates.Length - 1];
            }

            // set first graph date to the first of the first data month with no time component
            // the date axis always starts on the first of a month
            DateTime firstGraphDate = new DateTime(firstDataDate.Year, firstDataDate.Month, 1);

            // set last graph date to last data date
            DateTime lastGraphDate = lastDataDate;

            // if last graph date has a time component (this can happen if dates are averaged together), then set it to the next day with no time component
            if (lastGraphDate != lastGraphDate.Date)
            {
                if (lastGraphDate.Date != MaxDate)
                {
                    lastGraphDate = lastGraphDate.Date.AddDays(1);
                }
            }

            // if last grph date is not the first of the month or last graph date is same as first graph date, then set last graph date to the first of the following month
            // this ensures there will always be at least one month between first graph date and last graph date
            if (lastGraphDate.Day > 1 || lastGraphDate == firstGraphDate)
            {
                if (lastGraphDate.Month == 12)
                {
                    if (lastGraphDate.Year == 9999)
                    {
                        lastGraphDate = MaxDate;
                    }
                    else
                    {
                        lastGraphDate = new DateTime(lastGraphDate.Year + 1, 1, 1);
                    }
                }
                else
                {
                    lastGraphDate = new DateTime(lastGraphDate.Year, lastGraphDate.Month + 1, 1);
                }
            }

            // get first graph year
            int firstGraphYear = firstGraphDate.Year;

            // get last graph year
            // for January 1, use the year
            // for other than January 1, use the next year
            int lastGraphYear;
            if (lastGraphDate.Month == 1 && lastGraphDate.Day == 1)
            {
                lastGraphYear = lastGraphDate.Year;
            }
            else
            {
                lastGraphYear = lastGraphDate.Year + 1;
            }

            // compute number of years on the graph
            int graphYears = lastGraphYear - firstGraphYear;

            // compute number of months on the graph
            int firstGraphMonth = 12 * firstGraphDate.Year + firstGraphDate.Month;
            int lastGraphMonth = 12 * lastGraphDate.Year + lastGraphDate.Month;
            int graphMonths = lastGraphMonth - firstGraphMonth;

            // compute date increment unit/amount, start/end year, and start/end date
            if (graphYears > 4)
            {
                // for more than 4 years, increment by years and compute increment year amount
                _dateIncrement = DateIncrement.Years;
                _incrementYear = Mathf.CeilToInt(Mathf.Pow(10f, Mathf.FloorToInt(Mathf.Log10(0.5f * graphYears))));

                // compute start/end year
                _startYear = _incrementYear * Mathf.FloorToInt((float)firstGraphYear / _incrementYear);
                _endYear   = _incrementYear * Mathf.CeilToInt((float)lastGraphYear   / _incrementYear);

                // if more than 15 divisions, double the increment and recompute
                if ((float)(_endYear - _startYear) / _incrementYear > 15f)
                {
                    _incrementYear *= 2;
                    _startYear = _incrementYear * Mathf.FloorToInt((float)firstGraphYear / _incrementYear);
                    _endYear   = _incrementYear * Mathf.CeilToInt((float)lastGraphYear   / _incrementYear);
                }

                // if less than 5 divisions and increment divides evenly by 2, halve the increment and recompute
                if ((float)(_endYear - _startYear) / _incrementYear < 5f && _incrementYear % 2 == 0)
                {
                    _incrementYear /= 2;
                    _startYear = _incrementYear * Mathf.FloorToInt((float)firstGraphYear / _incrementYear);
                    _endYear   = _incrementYear * Mathf.CeilToInt((float)lastGraphYear   / _incrementYear);
                }

                // set start/end date to January 1 of the start/end year just computed
                _startDate = (_startYear == 0   ? MinDate : new DateTime(_startYear, 1, 1));
                _endDate   = (_endYear >= 10000 ? MaxDate : new DateTime(_endYear,   1, 1));
            }
            else if (graphMonths > 12)
            {
                // 13 months to 4 years
                if (graphYears == 4)
                {
                    // for 4 years, increment by 6 months:  year, Jul, year+1, Jul, year+2, Jul, year+3, Jul, year+4
                    _dateIncrement = DateIncrement.Months6;
                }
                else if (graphYears == 3)
                {
                    // for 3 years, increment by 3 months:  year, Apr, Jul, Oct, year+1, Apr, Jul, Oct, year+2, Apr, Jul, Oct, year+3
                    _dateIncrement = DateIncrement.Months3;
                }
                else
                {
                    // for 12 months to 2 years, increment by 2 months:  year, Mar, May, Jul, Sep, Nov, year+1, Mar, May, Jul, Sep, Nov, year+2
                    _dateIncrement = DateIncrement.Months2;
                }

                // set start/end date to January 1 of the first/last graph year
                _startDate = (firstGraphYear == 0    ? MinDate : new DateTime(firstGraphYear, 1, 1));
                _endDate   = (lastGraphYear >= 10000 ? MaxDate : new DateTime(lastGraphYear,  1, 1));
            }
            else
            {
                // 12 months or less
                if (graphMonths >= 6)
                {
                    // for 6 to 12 months, increment by 1 month
                    _dateIncrement = DateIncrement.Months1;
                }
                else if (graphMonths >= 3)
                {
                    // 3 to 5 months, increment by 10 days
                    // for 5 months:  Mon, 11, 21, Mon+1, 11, 21, Mon+2, 11, 21, Mon+3, 11, 21, Mon+4, 11, 21, Mon+5
                    // for 4 months:  Mon, 11, 21, Mon+1, 11, 21, Mon+2, 11, 21, Mon+3, 11, 21, Mon+4
                    // for 3 months:  Mon, 11, 21, Mon+1, 11, 21, Mon+2, 11, 21, Mon+3
                    _dateIncrement = DateIncrement.Days10;
                }
                else if (graphMonths == 2)
                {
                    // for 2 months, increment by 5 days:  Mon, 6, 11, 16, 21, 26, Mon+1, 6, 11, 16, 21, 26, Mon+2
                    _dateIncrement = DateIncrement.Days5;
                }
                else
                {
                    // for 1 month, increment by 2 days
                    // for a month with 31 days:  Mon, 3, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23, 25, 27, 29, Mon+1
                    // for a month with 30 days:  Mon, 3, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23, 25, 27, 29, Mon+1
                    // for a month with 29 days:  Mon, 3, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23, 25, 27, Mon+1
                    // for a month with 28 days:  Mon, 3, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23, 25, 27, Mon+1
                    _dateIncrement = DateIncrement.Days2;
                }

                // set start/end date to first/last graph date
                _startDate = firstGraphDate;
                _endDate = lastGraphDate;
            }

            // compute start, end, and graph range
            _startTicks = _startDate.Ticks;
            _endTicks = _endDate.Ticks;
            _graphTickRange = _endTicks - _startTicks;

            // reset curve min and max values
            _minCurveValue = double.MaxValue;
            _maxCurveValue = double.MinValue;
        }

        /// <summary>
        /// add a curve to the graph
        /// </summary>
        public void AddCurve(string description, string units, string numberFormat, double?[] data, float width, Color32 color)
        {
            // date must be set first
            if (_dates == null)
            {
                throw new InvalidOperationException("Dates must be set before adding a curve.");
            }
            if (data.Length != _dates.Length)
            {
                throw new InvalidOperationException("Curve data must have the same number of entries as the dates.");
            }

            // create a new curve
            CurveSettings curve = new CurveSettings(description, units, numberFormat, data, width, color);
            _curves.Add(curve);

            // compute new min and max values
            _minCurveValue = Math.Min(_minCurveValue, curve.MinValue);
            _maxCurveValue = Math.Max(_maxCurveValue, curve.MaxValue);

            // compute min value; if min value is less than 30% of max value, then use zero for min value
            double minValue = _minCurveValue;
            if (minValue > 0 && minValue < 0.3d * _maxCurveValue)
            {
                minValue = 0f;
            }

            // compute max value; if min and max values are same, set max value to min + 1
            double maxValue = _maxCurveValue;
            if (maxValue == minValue)
            {
                maxValue = Math.Floor(minValue) + 1d;
            }

            // compute whole number increment, must be at least 1
            _incrementValue = (long)Math.Ceiling(Math.Pow(10d, (long)Math.Floor(Math.Log10(0.5 * (maxValue - minValue)))));
            if (_incrementValue == 0d)
            {
                _incrementValue = 1d;
            }

            // compute start and end values
            _startValue = _incrementValue * Math.Floor(minValue / _incrementValue);
            _endValue = _incrementValue * Math.Ceiling(maxValue / _incrementValue);

            // if more than 15 divisions, double the increment and recompute
            if ((_endValue - _startValue) / _incrementValue > 15d)
            {
                _incrementValue *= 2d;
                _startValue = _incrementValue * Math.Floor(minValue / _incrementValue);
                _endValue = _incrementValue * Math.Ceiling(maxValue / _incrementValue);
            }

            // if less than 5 divisions and increment divides evenly by 2, halve the increment and recompute
            if ((_endValue - _startValue) / _incrementValue < 5d && _incrementValue % 2d == 0)
            {
                _incrementValue /= 2d;
                _startValue = _incrementValue * Math.Floor(minValue / _incrementValue);
                _endValue = _incrementValue * Math.Ceiling(maxValue / _incrementValue);
            }

            // compute graph range, if range is 1,2,3, then use an increment smaller than 1
            _graphValueRange = _endValue - _startValue;
            if (_graphValueRange == 1d)
            {
                _incrementValue = 0.1d;
            }
            else if (_graphValueRange == 2d)
            {
                _incrementValue = 0.2d;
            }
            else if (_graphValueRange == 3d)
            {
                _incrementValue = 0.5d;
            }
        }

        /// <summary>
        /// clear the graph
        /// </summary>
        public void Clear()
        {
            _dates = null;
            _curves.Clear();
            Invalidate();
        }

        /// <summary>
        /// when the user hovers the cursor near a data point, show data for the point
        /// </summary>
        protected override void OnTooltipHover(UIMouseEventParameter p)
        {
            // assume no data point found
            bool foundDataPoint = false;

            // there must be at least one curve
            if (_curves.Count >= 1)
            {
                PixelsToUnits();

                // compute the cursor position relative to the graph rect
                Vector2 hitPosition = GetHitPosition(p);
                hitPosition.x /= size.x;
                hitPosition.y /= size.y;
                hitPosition.x = (     hitPosition.x - _graphRect.xMin) / _graphRect.width;
                hitPosition.y = (1f - hitPosition.y - _graphRect.yMin) / _graphRect.height;

                // cursor must be in the graph rect
                if (hitPosition.x >= 0f && hitPosition.x <= 1f && hitPosition.y >= 0f && hitPosition.y <= 1f)
                {
                    // compute date index according to X hit position
                    const double MinTooltipDistance = 0.01d;
                    int dateIndex = -1;
                    double minDistanceX = MinTooltipDistance;
                    for (int i = 0; i < _dates.Length; i++)
                    {
                        double posX = (double)(_dates[i].Ticks - _startTicks) / _graphTickRange;
                        double distanceX = Math.Abs(posX - hitPosition.x);
                        if (distanceX < minDistanceX)
                        {
                            dateIndex = i;
                            minDistanceX = distanceX;
                        }
                    }

                    // date must be found
                    if (dateIndex >= 0)
                    {
                        // compute curve index by finding the curve with a data point closest to the Y hit position
                        int curveIndex = -1;
                        double minDistanceY = MinTooltipDistance;
                        for (int i = 0; i < _curves.Count; i++)
                        {
                            double? dataValue = _curves[i].Data[dateIndex];
                            if (dataValue.HasValue)
                            {
                                double posY = (dataValue.Value - _startValue) / _graphValueRange;
                                double distanceY = Math.Abs(posY - hitPosition.y);
                                if (distanceY < minDistanceY)
                                {
                                    curveIndex = i;
                                    minDistanceY = distanceY;
                                }
                            }
                        }

                        // curve must be found
                        if (curveIndex >= 0)
                        {
                            // found a data point
                            foundDataPoint = true;

                            // set the tool tip text
                            CurveSettings curve = _curves[curveIndex];
                            m_Tooltip = curve.Description + Environment.NewLine +
                                        $"{_dates[dateIndex].ToString("dd/MM/yyyy")}:  {curve.Data[dateIndex].Value.ToString(curve.NumberFormat, LocaleManager.cultureInfo)} {curve.Units}";

                            // compute the tool tip box position to follow the cursor
                            UIView uIView = GetUIView();
                            Vector2 cursorPositionOnScreen = uIView.ScreenPointToGUI(p.position / uIView.inputScale);
                            Vector3 vector3 = tooltipBox.pivot.UpperLeftToTransform(tooltipBox.size, tooltipBox.arbitraryPivotOffset);
                            Vector2 tooltipPosition = cursorPositionOnScreen + new Vector2(vector3.x, vector3.y);

                            // make sure tooltip box is entirely on the screen
                            Vector2 screenResolution = uIView.GetScreenResolution();
                            if (tooltipPosition.x < 0f)
                            {
                                tooltipPosition.x = 0f;
                            }
                            if (tooltipPosition.y < 0f)
                            {
                                tooltipPosition.y = 0f;
                            }
                            if (tooltipPosition.x + tooltipBox.width > screenResolution.x)
                            {
                                tooltipPosition.x = screenResolution.x - tooltipBox.width;
                            }
                            if (tooltipPosition.y + tooltipBox.height > screenResolution.y)
                            {
                                tooltipPosition.y = screenResolution.y - tooltipBox.height;
                            }
                            tooltipBox.relativePosition = tooltipPosition;
                        }
                    }
                }
            }

            // check if data point was found
            if (foundDataPoint)
            {
                base.OnTooltipHover(p);
            }
            else
            {
                base.OnTooltipLeave(p);
            }
            RefreshTooltip();
        }

        /// <summary>
        /// called when graph needs to be rendered
        /// </summary>
        protected override void OnRebuildRenderData()
        {
            try
            {
                // make sure font is defined and valid
                if (_font == null || !_font.isValid)
                {
                    _font = GetUIView().defaultFont;
                }

                // proceed only if things needed to render are valid
                if (atlas != null && atlas.material != null && _font != null && _font.isValid && isVisible && spriteInfo != null)
                {
                    // clear the text render
                    textRenderData.Clear();

                    // copy material from base atlas
                    renderData.material = atlas.material;
                    textRenderData.material = atlas.material;

                    // get items from base render data
                    PoolList<Vector3> vertices = renderData.vertices;
                    PoolList<int> triangles = renderData.triangles;
                    PoolList<Vector2> uvs = renderData.uvs;
                    PoolList<Color32> colors = renderData.colors;

                    // draw axes and labels
                    DrawAxesAndLabels(vertices, triangles, uvs, colors);

                    // draw each curve
                    foreach (CurveSettings curve in _curves)
                    {
                        DrawCurve(vertices, triangles, uvs, colors, curve);
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogException(ex);
            }
        }

        /// <summary>
        /// compute the X position of the date on the graph
        /// </summary>
        private float NormalizeDate(DateTime date)
        {
            return -0.5f + _graphRect.xMin + _graphRect.width * (date.Ticks - _startTicks) / _graphTickRange;
        }

        /// <summary>
        /// compute the Y position of the value on the graph
        /// </summary>
        private float NormalizeValue(double value)
        {
            return (float)(-0.5f + _graphRect.yMin + _graphRect.height * (value - _startValue) / _graphValueRange);
        }

        /// <summary>
        /// draw an axis line
        /// logic adapted from UIGraph.AddSolidQuad
        /// </summary>
        private void DrawAxisLine(Vector2 corner1, Vector2 corner2, Color32 col, PoolList<Vector3> vertices, PoolList<int> triangles, PoolList<Vector2> uvs, PoolList<Color32> colors)
        {
            // ignore if no sprite info
            if (spriteInfo == null)
            {
                return;
            }

            // draw a solid line
            Rect region = spriteInfo.region;
            uvs.Add(new Vector2(0.75f * region.xMin + 0.25f * region.xMax, 0.75f * region.yMin + 0.25f * region.yMax));
            uvs.Add(new Vector2(0.25f * region.xMin + 0.75f * region.xMax, 0.75f * region.yMin + 0.25f * region.yMax));
            uvs.Add(new Vector2(0.25f * region.xMin + 0.75f * region.xMax, 0.25f * region.yMin + 0.75f * region.yMax));
            uvs.Add(new Vector2(0.75f * region.xMin + 0.25f * region.xMax, 0.25f * region.yMin + 0.75f * region.yMax));
            vertices.Add(new Vector3(corner1.x, corner1.y));
            vertices.Add(new Vector3(corner2.x, corner1.y));
            vertices.Add(new Vector3(corner2.x, corner2.y));
            vertices.Add(new Vector3(corner1.x, corner2.y));
            AddTriangles(triangles, vertices.Count);
            colors.Add(col);
            colors.Add(col);
            colors.Add(col);
            colors.Add(col);
        }

        /// <summary>
        /// draw the axes and labels on the graph
        /// logic is adapted from UIGraph.BuildLabels
        /// </summary>
        private void DrawAxesAndLabels(PoolList<Vector3> vertices, PoolList<int> triangles, PoolList<Vector2> uvs, PoolList<Color32> colors)
        {
            // ignore if no curves
            if (_curves.Count == 0)
            {
                return;
            }

            // compute some values used often
            float pixelRatio = PixelsToUnits();
            float ratioXY = size.x / size.y;
            Vector3 baseSize = pixelRatio * size;
            Vector2 maxTextSize = new Vector2(size.x, size.y);
            Vector3 center = pivot.TransformToCenter(size, arbitraryPivotOffset) * pixelRatio;

            // some variables that get re-used
            float lineWidth;
            Color32 lineColor;
            float normalizedX;
            float normalizedY;
            Vector2 corner1;
            Vector2 corner2;

            // draw each horizontal line and the value labels to the left
            lineWidth = AxesWidth;
            lineColor = AxesColor;
            string numberformat = _incrementValue < 1d ? "N1" : "N0";
            for (double value = _startValue; value <= _endValue + 0.01d; value += _incrementValue)
            {
                // compute normalized Y value
                normalizedY = NormalizeValue(value);

                // for unknown reasons, must obtain the renderer again for each label to ensure a value with a space separator (e.g. French "1 234") is rendered correctly
                using (UIFontRenderer uIFontRenderer = _font.ObtainRenderer())
                {
                    // draw the value label
                    uIFontRenderer.textScale = 1f;
                    uIFontRenderer.vectorOffset = new Vector3(0f, (0f - height) * pixelRatio * (0.5f - normalizedY) + pixelRatio * 8f, 0f);
                    uIFontRenderer.pixelRatio = pixelRatio;
                    uIFontRenderer.maxSize = maxTextSize;
                    uIFontRenderer.defaultColor = TextColor;
                    uIFontRenderer.Render(value.ToString(numberformat, LocaleManager.cultureInfo), textRenderData);
                }

                // draw axis line
                corner1 = new Vector2(-0.5f + _graphRect.xMin, normalizedY - pixelRatio * lineWidth);
                corner2 = new Vector2(corner1.x + _graphRect.width, normalizedY + pixelRatio * lineWidth);
                DrawAxisLine(Vector3.Scale(corner1, baseSize) + center, Vector3.Scale(corner2, baseSize) + center, lineColor, vertices, triangles, uvs, colors);

                // first line is main axis line; subsequent lines are helper lines
                lineWidth = HelpAxesWidth;
                lineColor = HelpAxesColor;
            }

            // draw each vertical line and the labels below
            lineWidth = AxesWidth;
            lineColor = AxesColor;
            float yPos = height * pixelRatio * (-1f + _graphRect.yMin / 2f) + pixelRatio * 4f;
            if (_dateIncrement == DateIncrement.Years)
            {
                // do each year
                for (int year = _startYear; year <= _endYear; year += _incrementYear)
                {
                    // compute normalized X value
                    DateTime date;
                    if (year < 1)
                    {
                        date = new DateTime(1, 1, 1);
                    }
                    else if (year > 9999)
                    {
                        date = new DateTime(9999, 12, 31);
                    }
                    else
                    {
                        date = new DateTime(year, 1, 1);
                    }
                    normalizedX = NormalizeDate(date);

                    // render date label
                    RenderDateLabel(normalizedX, yPos, maxTextSize, year.ToString());

                    // draw axis line
                    corner1 = new Vector2(normalizedX - pixelRatio * lineWidth / ratioXY, -0.5f + _graphRect.yMin);
                    corner2 = new Vector2(normalizedX + pixelRatio * lineWidth / ratioXY, corner1.y + _graphRect.height);
                    DrawAxisLine(Vector3.Scale(corner1, baseSize) + center, Vector3.Scale(corner2, baseSize) + center, lineColor, vertices, triangles, uvs, colors);

                    // first line is main axis line; subsequent lines are helper lines
                    lineWidth = HelpAxesWidth;
                    lineColor = HelpAxesColor;
                }
            }
            else
            {
                // do each month or day
                DateTime date = _startDate;
                while (date <= _endDate)
                {
                    // compute normalized X value
                    normalizedX = NormalizeDate(date);

                    // render date label
                    string dateLabel;
                    if (_dateIncrement == DateIncrement.Months6 || _dateIncrement == DateIncrement.Months3 || _dateIncrement == DateIncrement.Months2 || _dateIncrement == DateIncrement.Months1)
                    {
                        // for Dec 31, 9999, show year 10000
                        if (date == MaxDate)
                        {
                            dateLabel = "10000";
                        }
                        // for January 1, show year
                        else if (date.Month == 1 && date.Day == 1)
                        {
                            dateLabel = date.Year.ToString();
                        }
                        // for other dates, show month name
                        else
                        {
                            dateLabel = GetMonthLabel(date);
                        }
                    }
                    else
                    {
                        // for first of the month, show month name
                        if (date.Day == 1)
                        {
                            dateLabel = GetMonthLabel(date);
                        }
                        // for other days, show day number
                        else
                        {
                            dateLabel = date.Day.ToString();
                        }
                    }
                    RenderDateLabel(normalizedX, yPos, maxTextSize, dateLabel);

                    // draw axis line
                    corner1 = new Vector2(normalizedX - pixelRatio * lineWidth / ratioXY, -0.5f + _graphRect.yMin);
                    corner2 = new Vector2(normalizedX + pixelRatio * lineWidth / ratioXY, corner1.y + _graphRect.height);
                    DrawAxisLine(Vector3.Scale(corner1, baseSize) + center, Vector3.Scale(corner2, baseSize) + center, lineColor, vertices, triangles, uvs, colors);

                    // first line is main axis line; subsequent lines are helper lines
                    lineWidth = HelpAxesWidth;
                    lineColor = HelpAxesColor;

                    // compute next date according to date increment
                    try
                    {
                        if (_dateIncrement == DateIncrement.Months6)
                        {
                            // increment by 6 months
                            date = date.AddMonths(6);
                        }
                        else if (_dateIncrement == DateIncrement.Months3)
                        {
                            // increment by 3 months
                            date = date.AddMonths(3);
                        }
                        else if (_dateIncrement == DateIncrement.Months2)
                        {
                            // increment by 2 months
                            date = date.AddMonths(2);
                        }
                        else if (_dateIncrement == DateIncrement.Months1)
                        {
                            // increment by 1 month
                            date = date.AddMonths(1);
                        }
                        else  if (_dateIncrement == DateIncrement.Days10)
                        {
                            // if at last day of month, use first day of next month, otherwise increment by 10 days
                            if (date.Day == 21)
                            {
                                date = new DateTime(date.Year, date.Month, 1).AddMonths(1);
                            }
                            else
                            {
                                date = date.AddDays(10);
                            }
                        }
                        else  if (_dateIncrement == DateIncrement.Days5)
                        {
                            // if at last day of month, use first day of next month, otherwise increment by 5 days
                            if (date.Day == 26)
                            {
                                date = new DateTime(date.Year, date.Month, 1).AddMonths(1);
                            }
                            else
                            {
                                date = date.AddDays(5);
                            }
                        }
                        else
                        {
                            // if at last day of month, use first day of next month, otherwise increment by 2 days
                            int daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
                            int dateDay = date.Day;
                            if ((daysInMonth == 31 && dateDay == 29) ||
                                (daysInMonth == 30 && dateDay == 29) ||
                                (daysInMonth == 29 && dateDay == 27) ||
                                (daysInMonth == 28 && dateDay == 27))
                            {
                                date = new DateTime(date.Year, date.Month, 1).AddMonths(1);
                            }
                            else
                            {
                                date = date.AddDays(2);
                            }
                        }
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // computing next date exceeded max value
                        // if already at max date, then break out of loop, otherwise use max date
                        if (date == MaxDate)
                        {
                            break;
                        }
                        date = MaxDate;
                    }
                }
            }

            // draw the horizontal axis line again so it covers the bottom ends of the vertical lines
            lineWidth = AxesWidth;
            lineColor = AxesColor;
            normalizedY = NormalizeValue(_startValue);
            corner1 = new Vector2(-0.5f + _graphRect.xMin, normalizedY - pixelRatio * lineWidth);
            corner2 = new Vector2(corner1.x + _graphRect.width, normalizedY + pixelRatio * lineWidth);
            DrawAxisLine(Vector3.Scale(corner1, baseSize) + center, Vector3.Scale(corner2, baseSize) + center, lineColor, vertices, triangles, uvs, colors);
        }

        /// <summary>
        /// get translated month label
        /// </summary>
        private string GetMonthLabel(DateTime date)
        {
            return Translations.instance.Miscellaneous.Get("Month" + date.Month);
        }

        /// <summary>
        /// render a date label below the graph
        /// </summary>
        private void RenderDateLabel(float xPos, float yPos, Vector2 maxTextSize, string dateText)
        {
            float pixelRatio = PixelsToUnits();
            using (UIFontRenderer uIFontRenderer = _font.ObtainRenderer())
            {
                // draw the date label
                uIFontRenderer.textScale = 1f;
                uIFontRenderer.vectorOffset = new Vector3(width * pixelRatio * xPos, yPos, 0f);
                uIFontRenderer.pixelRatio = pixelRatio;
                uIFontRenderer.maxSize = maxTextSize;
                uIFontRenderer.textAlign = UIHorizontalAlignment.Center;
                uIFontRenderer.defaultColor = TextColor;
                uIFontRenderer.Render(dateText, textRenderData);
            }
        }

        /// <summary>
        /// draw a curve on the graph
        /// logic adapted from UIGraph.BuildMeshData
        /// </summary>
        private void DrawCurve(PoolList<Vector3> vertices, PoolList<int> triangles, PoolList<Vector2> uvs, PoolList<Color32> colors, CurveSettings curve)
        {
            // ignore if no sprite info
            if (spriteInfo == null)
            {
                return;
            }

            // ignore if no data
            if (_dates.Length == 0)
            {
                return;
            }

            using (PoolList<Vector2> uvsLine = PoolList<Vector2>.Obtain())
            {
                // compute uvs for a line
                Rect region = spriteInfo.region;
                uvsLine.Add(new Vector2(0.75f * region.xMin + 0.25f * region.xMax, 0.75f * region.yMin + 0.25f * region.yMax));
                uvsLine.Add(new Vector2(0.25f * region.xMin + 0.75f * region.xMax, 0.75f * region.yMin + 0.25f * region.yMax));
                uvsLine.Add(new Vector2(0.25f * region.xMin + 0.75f * region.xMax, 0.25f * region.yMin + 0.75f * region.yMax));
                uvsLine.Add(new Vector2(0.75f * region.xMin + 0.25f * region.xMax, 0.25f * region.yMin + 0.75f * region.yMax));

                using (PoolList<Vector2> uvsDot = PoolList<Vector2>.Obtain())
                {
                    // compute uvs for a dot
                    uvsDot.Add(new Vector2(region.xMin, region.yMin));
                    uvsDot.Add(new Vector2(region.xMax, region.yMin));
                    uvsDot.Add(new Vector2(region.xMax, region.yMax));
                    uvsDot.Add(new Vector2(region.xMin, region.yMax));

                    using (PoolList<Color32> colorsLine = PoolList<Color32>.Obtain())
                    {
                        // compute colors for a line
                        colorsLine.Add(curve.Color);
                        colorsLine.Add(curve.Color);
                        colorsLine.Add(curve.Color);
                        colorsLine.Add(curve.Color);

                        using (PoolList<Color32> colorsDot = PoolList<Color32>.Obtain())
                        {
                            // compute colors for a dot
                            // dot color is a darker version of the curve color
                            const float DotColorMultiplier = 0.6f;
                            Color32 dotColor = new Color32((byte)(curve.Color.r * DotColorMultiplier), (byte)(curve.Color.g * DotColorMultiplier), (byte)(curve.Color.b * DotColorMultiplier), 255);
                            colorsDot.Add(dotColor);
                            colorsDot.Add(dotColor);
                            colorsDot.Add(dotColor);
                            colorsDot.Add(dotColor);

                            // compute some values used often
                            float pixelRatio = PixelsToUnits();
                            float ratioXY = size.x / size.y;
                            Vector3 baseSize = pixelRatio * size;
                            Vector3 center = pivot.TransformToCenter(size, arbitraryPivotOffset) * pixelRatio;

                            // compute the X and Y locations of the first data point
                            double? previousData = curve.Data[0];
                            Vector3 previousPoint = default;
                            previousPoint.x = NormalizeDate(_dates[0]);
                            previousPoint.y = NormalizeValue(previousData ?? 0f);

                            // do each data point starting with 1
                            for (int i = 1; i < curve.Data.Length; i++)
                            {
                                // compute the X and Y locations of the current point
                                double? currentData = curve.Data[i];
                                Vector3 currentPoint = default;
                                currentPoint.x = NormalizeDate(_dates[i]);
                                currentPoint.y = NormalizeValue(currentData ?? 0f);

                                // if previous and current data points have values, draw a line between the points
                                if (previousData.HasValue && currentData.HasValue)
                                {
                                    // compute distances between current and previous points
                                    float distanceX = currentPoint.x - previousPoint.x;
                                    float distanceY = currentPoint.y - previousPoint.y;
                                    float distanceXY = Mathf.Sqrt(distanceX * distanceX + distanceY * distanceY);

                                    // draw a line from previous point to current point
                                    uvs.AddRange(uvsLine);
                                    Vector3 vectorLine = default;
                                    vectorLine.x = pixelRatio * curve.Width * distanceY / (distanceXY * ratioXY);
                                    vectorLine.y = (0f - pixelRatio) * curve.Width * distanceX / distanceXY;
                                    vertices.Add(Vector3.Scale(previousPoint + vectorLine, baseSize) + center);
                                    vertices.Add(Vector3.Scale(currentPoint  + vectorLine, baseSize) + center);
                                    vertices.Add(Vector3.Scale(currentPoint  - vectorLine, baseSize) + center);
                                    vertices.Add(Vector3.Scale(previousPoint - vectorLine, baseSize) + center);
                                    AddTriangles(triangles, vertices.Count);
                                    colors.AddRange(colorsLine);
                                }

                                // if previous point has a value, draw a dot on it
                                if (previousData.HasValue)
                                {
                                    uvs.AddRange(uvsDot);
                                    Vector3 vectorDot1 = new Vector3(pixelRatio * curve.Width / ratioXY, 0f, 0f);
                                    Vector3 vectorDot2 = new Vector3(0f, pixelRatio * curve.Width, 0f);
                                    vertices.Add(Vector3.Scale(previousPoint - vectorDot1 - vectorDot2, baseSize) + center);
                                    vertices.Add(Vector3.Scale(previousPoint + vectorDot1 - vectorDot2, baseSize) + center);
                                    vertices.Add(Vector3.Scale(previousPoint + vectorDot1 + vectorDot2, baseSize) + center);
                                    vertices.Add(Vector3.Scale(previousPoint - vectorDot1 + vectorDot2, baseSize) + center);
                                    AddTriangles(triangles, vertices.Count);
                                    colors.AddRange(colorsDot);
                                }

                                // copy current to previous
                                previousPoint = currentPoint;
                                previousData = curve.Data[i];
                            }

                            // if last point has a value, draw a dot on it
                            if (previousData.HasValue)
                            {
                                // draw the dot
                                uvs.AddRange(uvsDot);
                                Vector3 vectorDot1 = new Vector3(pixelRatio * curve.Width / ratioXY, 0f, 0f);
                                Vector3 vectorDot2 = new Vector3(0f, pixelRatio * curve.Width, 0f);
                                vertices.Add(Vector3.Scale(previousPoint - vectorDot1 - vectorDot2, baseSize) + center);
                                vertices.Add(Vector3.Scale(previousPoint + vectorDot1 - vectorDot2, baseSize) + center);
                                vertices.Add(Vector3.Scale(previousPoint + vectorDot1 + vectorDot2, baseSize) + center);
                                vertices.Add(Vector3.Scale(previousPoint - vectorDot1 + vectorDot2, baseSize) + center);
                                AddTriangles(triangles, vertices.Count);
                                colors.AddRange(colorsDot);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// add triangles based on the vertices count
        /// </summary>
        private void AddTriangles(PoolList<int> triangles, int verticesCount)
        {
            triangles.Add(verticesCount - 4);
            triangles.Add(verticesCount - 3);
            triangles.Add(verticesCount - 2);
            triangles.Add(verticesCount - 4);
            triangles.Add(verticesCount - 2);
            triangles.Add(verticesCount - 1);
        }
    }
}
