﻿using System;
using System.Runtime.InteropServices;
using System.Linq;
using Microsoft.Win32;
using CSDeskBand.Interop;
using CSDeskBand.Interop.COM;
using CSDeskBand.Logging;
using static CSDeskBand.Interop.DESKBANDINFO.DBIM;
using static CSDeskBand.Interop.DESKBANDINFO.DBIMF;
using static CSDeskBand.Interop.DESKBANDINFO.DBIF;

namespace CSDeskBand
{
    /// <summary>
    /// Default implementation for icsdeskband
    /// </summary>
    internal class CSDeskBandImpl : ICSDeskBand
    {
        public static readonly int S_OK = 0;
        public static readonly int E_NOTIMPL = unchecked((int)0x80004001);

        public event EventHandler<VisibilityChangedEventArgs> VisibilityChanged;
        public event EventHandler Closed;

        public CSDeskBandOptions Options { get; }
        public TaskbarInfo TaskbarInfo { get; }

        private readonly IntPtr _handle;
        private IntPtr _parentWindowHandle;
        private object _parentSite; //Has these interfaces: IInputObjectSite, IOleWindow, IOleCommandTarget, IBandSite
        private uint _id;
        private Guid CGID_DeskBand = new Guid("EB0FE172-1A3A-11D0-89B3-00A0C90A90AC"); //Command group id for deskband. Used for IOleCommandTarge.Exec
        private readonly ILog _logger;
        private static readonly Guid CATID_DESKBAND = new Guid("00021492-0000-0000-C000-000000000046");

        public CSDeskBandImpl(IntPtr handle, CSDeskBandOptions options)
        {
            _logger = LogProvider.GetCurrentClassLogger();
            _handle = handle;
            Options = options;
            Options.PropertyChanged += Options_PropertyChanged;
            TaskbarInfo = new TaskbarInfo();
        }

        private void Options_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_parentSite == null)
            {
                return;
            }
            _logger.Debug("Deskband options have changed");

            var parent = (IOleCommandTarget) _parentSite;
            //Set pvaln to the id that was passed in SetSite
            //When int is marshalled to variant, it is marshalled as VT_i4. See default marshalling for objects
            parent.Exec(ref CGID_DeskBand, (uint) tagDESKBANDCID.DBID_BANDINFOCHANGED, 0, _id, null);
        }

        public int GetWindow(out IntPtr phwnd)
        {
            phwnd = _handle;
            return S_OK;
        }

        public int ContextSensitiveHelp(bool fEnterMode)
        {
            return E_NOTIMPL;
        }

        public int ShowDW([In] bool fShow)
        {
            VisibilityChanged?.Invoke(this, new VisibilityChangedEventArgs { IsVisible = fShow });
            return S_OK;
        }

        public int CloseDW([In] uint dwReserved)
        {
            Closed?.Invoke(this, null);
            return S_OK;
        }

        public int ResizeBorderDW(RECT prcBorder, [In, MarshalAs(UnmanagedType.IUnknown)] IntPtr punkToolbarSite, bool fReserved)
        {
            //must return notimpl
            return E_NOTIMPL;
        }

        public int GetBandInfo(uint dwBandID, DESKBANDINFO.DBIF dwViewMode, ref DESKBANDINFO pdbi)
        {
            _id = dwBandID;

            //Sizing information is requested whenever the taskbar changes size/orientation
            if (pdbi.dwMask.HasFlag(DBIM_MINSIZE))
            {
                _logger.Debug("Deskband minsize requested");
                if (dwViewMode.HasFlag(DBIF_VIEWMODE_VERTICAL))
                {
                    pdbi.ptMinSize.Y = Options.MinVertical.Width;
                    pdbi.ptMinSize.X = Options.MinVertical.Height;
                }
                else
                {
                    pdbi.ptMinSize.X = Options.MinHorizontal.Width;
                    pdbi.ptMinSize.Y = Options.MinHorizontal.Height;
                }
            }

            if (pdbi.dwMask.HasFlag(DBIM_MAXSIZE))
            {
                _logger.Debug("Deskband maxsize requested");
                if (dwViewMode.HasFlag(DBIF_VIEWMODE_VERTICAL))
                {
                    pdbi.ptMaxSize.Y = Options.MaxVertical.Width;
                    pdbi.ptMaxSize.X = Options.MaxVertical.Height;
                }
                else
                {
                    pdbi.ptMaxSize.X = Options.MaxHorizontal.Width;
                    pdbi.ptMaxSize.Y = Options.MaxHorizontal.Height;
                }
            }

            // x member is ignored
            if (pdbi.dwMask.HasFlag(DBIM_INTEGRAL))
            {
                _logger.Debug("Deskband integral requested");
                pdbi.ptIntegral.Y = Options.Increment;
                pdbi.ptIntegral.X = 0;
            }

            if (pdbi.dwMask.HasFlag(DBIM_ACTUAL))
            {
                _logger.Debug("Deskband actual size requested");
                if (dwViewMode.HasFlag(DBIF_VIEWMODE_VERTICAL))
                {
                    pdbi.ptActual.Y = Options.Vertical.Width;
                    pdbi.ptActual.X = Options.Vertical.Height;
                }
                else
                {
                    pdbi.ptActual.X = Options.Horizontal.Width;
                    pdbi.ptActual.Y = Options.Horizontal.Height;
                }
            }

            if (pdbi.dwMask.HasFlag(DBIM_TITLE))
            {
                _logger.Debug("Deskband tile requested");
                pdbi.wszTitle = Options.ShowTitle ? Options.Title : "";
            }

            if (pdbi.dwMask.HasFlag(DBIM_MODEFLAGS))
            {
                pdbi.dwModeFlags = DBIMF_NORMAL;
                pdbi.dwModeFlags |= Options.AlwaysShowGripper ? DBIMF_ALWAYSGRIPPER : 0;
                pdbi.dwModeFlags |= Options.Fixed ? DBIMF_FIXED | DBIMF_NOGRIPPER : 0;
                pdbi.dwModeFlags |= Options.NoMargins ? DBIMF_NOMARGINS : 0;
                pdbi.dwModeFlags |= Options.Sunken ? DBIMF_DEBOSSED : 0;
                pdbi.dwModeFlags |= Options.Undeleteable ? DBIMF_UNDELETEABLE : 0;
                pdbi.dwModeFlags |= Options.VariableHeight ? DBIMF_VARIABLEHEIGHT : 0;
                pdbi.dwModeFlags |= Options.AddToFront ? DBIMF_ADDTOFRONT : 0;
                pdbi.dwModeFlags |= Options.NewRow ? DBIMF_BREAK : 0;
                pdbi.dwModeFlags |= Options.TopRow ? DBIMF_TOPALIGN : 0;
                pdbi.dwModeFlags &= ~DBIMF_BKCOLOR; //Don't use background color
            }

            TaskbarInfo.UpdateInfo();

            return S_OK;
        }

        public int CanRenderComposited(out bool pfCanRenderComposited)
        {
            pfCanRenderComposited = true;
            return S_OK;
        }

        public int SetCompositionState(bool fCompositionEnabled)
        {
            return S_OK;
        }

        public int GetCompositionState(out bool pfCompositionEnabled)
        {
            pfCompositionEnabled = true;
            return S_OK;
        }

        public void SetSite([In, MarshalAs(UnmanagedType.IUnknown)] object pUnkSite)
        {
            if (_parentSite != null)
            {
                Marshal.ReleaseComObject(_parentSite);
            }

            //pUnkSite null means deskband was closed
            if (pUnkSite == null)
            {
                _logger.Debug("Closing deskband");
                Closed?.Invoke(this, null);
                return;
            }

            var oleWindow = (IOleWindow)pUnkSite;
            oleWindow.GetWindow(out _parentWindowHandle);
            User32.SetParent(_handle, _parentWindowHandle);

            _parentSite = pUnkSite;
        }

        public void GetSite(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvSite)
        {
            ppvSite = _parentSite;
        }

        [ComRegisterFunction]
        public static void Register(Type t)
        {
            try
            {
                string guid = t.GUID.ToString("B");
                RegistryKey rkClass = Registry.ClassesRoot.CreateSubKey($@"CLSID\{guid}");
                rkClass.SetValue(null, GetToolbarName(t));

                RegistryKey rkCat = rkClass.CreateSubKey("Implemented Categories");
                rkCat.CreateSubKey(CATID_DESKBAND.ToString("B"));

                Console.WriteLine($"Succesfully registered deskband {GetToolbarName(t)} - GUID: {guid}");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to register deskband {GetToolbarName(t)} - {e}");
            }
        }

        [ComUnregisterFunction]
        public static void Unregister(Type t)
        {
            try
            {
                string guid = t.GUID.ToString("B");
                Registry.ClassesRoot.CreateSubKey(@"CLSID").DeleteSubKeyTree(guid);

                Console.WriteLine($"Successfully unregistered deskband {GetToolbarName(t)} - GUID: {guid}");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to unregister deskband {GetToolbarName(t)} - {e}");
            }
        }

        private static string GetToolbarName(Type t)
        {
            var registrationInfo = (CSDeskBandRegistrationAttribute[]) t.GetCustomAttributes(typeof(CSDeskBandRegistrationAttribute), true);
            return registrationInfo.FirstOrDefault()?.Name ?? t.Name;
        }
    }
}