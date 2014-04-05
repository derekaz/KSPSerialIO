﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using System.Runtime.InteropServices;

using OpenNETCF.IO.Ports;
using UnityEngine;
using KSP.IO;

namespace KSPSerialIO
{
    #region Structs
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VesselData
    {
        public byte id;             //1
        public float AP;            //2
        public float PE;            //3
        public float SemiMajorAxis; //4
        public float SemiMinorAxis; //5
        public float VVI;           //6
        public float e;             //7
        public float inc;           //8
        public float G;             //9
        public int TAp;             //10
        public int TPe;             //11
        public float TrueAnomaly;   //12
        public float Density;       //13
        public int period;          //14
        public float RAlt;          //15
        public float Alt;           //16
        public float Vsurf;         //17
        public float Lat;           //18
        public float Lon;           //19
        public float LiquidFuelTot; //20
        public float LiquidFuel;    //21
        public float OxidizerTot;   //22
        public float Oxidizer;      //23
        public float EChargeTot;    //24
        public float ECharge;       //25
        public float MonoPropTot;   //26
        public float MonoProp;      //27
        public float IntakeAirTot;  //28
        public float IntakeAir;     //29
        public float SolidFuelTot;  //30
        public float SolidFuel;     //31
        public float XenonGasTot;   //32
        public float XenonGas;      //33
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HandShakePacket
    {
        public byte id;
        public byte M1;
        public byte M2;
        public byte M3;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ControlPacket
    {
        public byte id;
        public byte MainControls;                  //SAS RCS Lights Gear Brakes Precision Abort Stage 
        public byte Mode;                          //0 = stage, 1 = docking, 2 = map
        public ushort ControlGroup;                //control groups 1-10 in 2 bytes
        public byte AdditionalControlByte1;        //other stuff
        public byte AdditionalControlByte2;
        public short Pitch;                        //-1000 -> 1000
        public short Roll;                         //-1000 -> 1000
        public short Yaw;                          //-1000 -> 1000
        public short TX;                           //-1000 -> 1000
        public short TY;                           //-1000 -> 1000
        public short TZ;                           //-1000 -> 1000
        public short Throttle;                     //    0 -> 1000
    };

    public struct VesselControls
    {
        public Boolean SAS;
        public Boolean RCS;
        public Boolean Lights;
        public Boolean Gear;
        public Boolean Brakes;
        public Boolean Precision;
        public Boolean Abort;
        public Boolean Stage;
        public int Mode;
        public Boolean[] ControlGroup;
        public float Pitch;
        public float Roll;
        public float Yaw;
        public float TX;
        public float TY;
        public float TZ;
        public float Throttle;
    };

    struct IOResource
    {
        public float Max;
        public float Current;
    }

    #endregion

    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public class SettingsNStuff : MonoBehaviour
    {
        public static PluginConfiguration cfg = PluginConfiguration.CreateForType<SettingsNStuff>();
        public static string DefaultPort;
        public static double refreshrate;
        public static int HandshakeDelay;
        public static int BaudRate;
        public static bool ThrottleEnable;
        public static bool PitchEnable;
        public static bool RollEnable;
        public static bool YawEnable;
        public static bool TXEnable;
        public static bool TYEnable;
        public static bool TZEnable;
        public static double SASTol;

        void Awake()
        {
            //cfg["refresh"] = 0.08;
            //cfg["DefaultPort"] = "COM1";
            //cfg["HandshakeDelay"] = 2500;
            print("KSPSerialIO: Loading settings...");

            cfg.load();
            DefaultPort = cfg.GetValue<string>("DefaultPort");
            print("KSPSerialIO: Default Port = " + DefaultPort);

            refreshrate = cfg.GetValue<double>("refresh");
            print("KSPSerialIO: Refreshrate = " + refreshrate.ToString());

            BaudRate = cfg.GetValue<int>("BaudRate");
            print("KSPSerialIO: BaudRate = " + BaudRate.ToString());

            HandshakeDelay = cfg.GetValue<int>("HandshakeDelay");
            print("KSPSerialIO: Handshake Delay = " + HandshakeDelay.ToString());

            ThrottleEnable = cfg.GetValue<bool>("ThrottleEnable");
            print("KSPSerialIO: Throttle Enable = " + ThrottleEnable.ToString());

            PitchEnable = cfg.GetValue<bool>("PitchEnable");
            print("KSPSerialIO: Pitch Enable = " + PitchEnable.ToString());

            RollEnable = cfg.GetValue<bool>("RollEnable");
            print("KSPSerialIO: Roll Enable = " + RollEnable.ToString());

            YawEnable = cfg.GetValue<bool>("YawEnable");
            print("KSPSerialIO: Yaw Enable = " + YawEnable.ToString());

            TXEnable = cfg.GetValue<bool>("TXEnable");
            print("KSPSerialIO: Translate X Enable = " + TXEnable.ToString());

            TYEnable = cfg.GetValue<bool>("TYEnable");
            print("KSPSerialIO: Translate Y Enable = " + TYEnable.ToString());

            TZEnable = cfg.GetValue<bool>("TZEnable");
            print("KSPSerialIO: Translate Z Enable = " + TZEnable.ToString());

            SASTol = cfg.GetValue<double>("SASTol");
            print("KSPSerialIO: SAS Tol = " + SASTol.ToString());
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KSPSerialPort : MonoBehaviour
    {
        public static SerialPort Port;
        public static string PortNumber;
        public static Boolean DisplayFound = false;
        public static Boolean ControlReceived = false;

        public static VesselData VData;
        public static ControlPacket CPacket;
        private static HandShakePacket HPacket;

        public static VesselControls VControls = new VesselControls();
        public static VesselControls VControlsOld = new VesselControls();

        private static byte[] buffer = new byte[255];
        private static byte rx_len;
        private static byte rx_array_inx;
        private static int structSize;
        private static byte id = 255;

        public static void sendPacket(object anything)
        {
            byte[] Payload = StructureToByteArray(anything);
            byte header1 = 0xBE;
            byte header2 = 0xEF;
            byte size = (byte)Payload.Length;
            byte checksum = size;

            byte[] Packet = new byte[size + 4];

            //Packet = [header][size][payload][checksum];
            //Header = [Header1=0xBE][Header2=0xEF]
            //size = [payload.length (0-255)]

            for (int i = 0; i < size; i++)
            {
                checksum ^= Payload[i];
            }

            Payload.CopyTo(Packet, 3);
            Packet[0] = header1;
            Packet[1] = header2;
            Packet[2] = size;
            Packet[Packet.Length - 1] = checksum;

            Port.Write(Packet, 0, Packet.Length);
        }

        private void Begin()
        {
            Port = new SerialPort(PortNumber, SettingsNStuff.BaudRate, Parity.None, 8, StopBits.One);
            Port.ReceivedBytesThreshold = 3;
            Port.ReceivedEvent += Port_ReceivedEvent;
        }

        //these are copied from the intarwebs, converts struct to byte array
        private static byte[] StructureToByteArray(object obj)
        {
            int len = Marshal.SizeOf(obj);
            byte[] arr = new byte[len];
            IntPtr ptr = Marshal.AllocHGlobal(len);
            Marshal.StructureToPtr(obj, ptr, true);
            Marshal.Copy(ptr, arr, 0, len);
            Marshal.FreeHGlobal(ptr);
            return arr;
        }

        private static object ByteArrayToStructure(byte[] bytearray, object obj)
        {
            int len = Marshal.SizeOf(obj);

            IntPtr i = Marshal.AllocHGlobal(len);

            Marshal.Copy(bytearray, 0, i, len);

            obj = Marshal.PtrToStructure(i, obj.GetType());

            Marshal.FreeHGlobal(i);

            return obj;
        }
        /*
        private static T ReadUsingMarshalUnsafe<T>(byte[] data) where T : struct
        {
            unsafe
            {
                fixed (byte* p = &data[0])
                {
                    return (T)Marshal.PtrToStructure(new IntPtr(p), typeof(T));
                }
            }
        }
        */
        void initializeDataPackets()
        {
            VData = new VesselData();
            VData.id = 1;

            HPacket = new HandShakePacket();
            HPacket.id = 0;
            HPacket.M1 = 1;
            HPacket.M2 = 2;
            HPacket.M3 = 3;

            CPacket = new ControlPacket();

            VControls.ControlGroup = new Boolean[11];
            VControlsOld.ControlGroup = new Boolean[11];
        }

        void Awake()
        {
            if (DisplayFound)
            {
                Debug.Log("KSPSerialIO: running...");
                Begin();
            }
            else
            {
                Debug.Log("KSPSerialIO: Version 0.14.2 ");
                Debug.Log("KSPSerialIO: Getting serial ports...");
                Debug.Log("KSPSerialIO: Output packet size: " + Marshal.SizeOf(VData).ToString() + "/255");
                initializeDataPackets();

                try
                {
                    //Use registry hack to get a list of serial ports until we get system.io.ports
                    RegistryKey SerialCOMSKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM\");

                    Begin();

                    //print("KSPSerialIO: receive threshold " + Port.ReceivedBytesThreshold.ToString());

                    if (SerialCOMSKey == null)
                    {
                        Debug.Log("KSPSerialIO: Dude do you even win32 serial port??");
                    }
                    else
                    {
                        String[] realports = SerialCOMSKey.GetValueNames();  // get list of all serial devices
                        String[] names = new string[realports.Length + 1];   // make a new list with 1 extra, we put the default port first
                        realports.CopyTo(names, 1);

                        Debug.Log("KSPSerialIO: Found " + names.Length.ToString() + " serial ports");

                        //look through all found ports for our display
                        int j = 0;

                        foreach (string PortName in names)
                        {
                            if (j == 0)  // try default port first
                            {
                                PortNumber = SettingsNStuff.DefaultPort;
                                Debug.Log("KSPSerialIO: trying default port " + PortNumber);
                            }
                            else
                            {
                                PortNumber = (string)SerialCOMSKey.GetValue(PortName);
                                Debug.Log("KSPSerialIO: trying port " + PortName + " - " + PortNumber);
                            }

                            Port.PortName = PortNumber;

                            j++;

                            if (!Port.IsOpen)
                            {
                                try
                                {
                                    Port.Open();
                                }
                                catch (Exception e)
                                {
                                    Debug.Log("Error opening serial port " + Port.PortName + ": " + e.Message);
                                }

                                //secret handshake
                                if (Port.IsOpen)
                                {
                                    Thread.Sleep(SettingsNStuff.HandshakeDelay);
                                    sendPacket(HPacket);

                                    //wait for reply
                                    int k = 0;
                                    while (Port.BytesToRead == 0 && k < 15 && !DisplayFound)
                                    {
                                        Thread.Sleep(100);
                                        k++;
                                    }

                                    Port.Close();
                                    if (DisplayFound)
                                    {
                                        Debug.Log("KSPSerialIO: found KSP Display at " + Port.PortName);
                                        break;
                                    }
                                    else
                                    {
                                        Debug.Log("KSPSerialIO: KSP Display not found");
                                    }
                                }
                            }
                            else
                            {
                                Debug.Log("KSPSerialIO: " + PortNumber + "is already being used.");
                            }
                        }
                    }

                }
                catch (Exception e)
                {
                    print(e.Message);
                }
            }
        }

        private string readline()
        {
            string result = null;
            char c;
            int j = 0;

            c = (char)Port.ReadByte();
            while (c != '\n' && j < 255)
            {
                result += c;
                c = (char)Port.ReadByte();
                j++;
            }
            return result;
        }

        private void Port_ReceivedEvent(object sender, SerialReceivedEventArgs e)
        {
            while (Port.BytesToRead > 0)
            {
                if (processCOM())
                {
                    switch (id)
                    {
                        case 0:
                            DisplayFound = true;
                            Invoke("HandShake", 0);
                            break;
                        case 1:
                            VesselControls();
                            //Invoke("VesselControls", 0);
                            break;
                        default:
                            Invoke("Unimplemented", 0);
                            break;
                    }
                }
            }
        }

        private static bool processCOM()
        {
            byte calc_CS;

            if (rx_len == 0)
            {
                while (Port.ReadByte() != 0xBE)
                {
                    if (Port.BytesToRead == 0)
                        return false;
                }

                if (Port.ReadByte() == 0xEF)
                {
                    rx_len = (byte)Port.ReadByte();
                    id = (byte)Port.ReadByte();
                    rx_array_inx = 1;

                    switch (id)
                    {
                        case 0:
                            structSize = Marshal.SizeOf(HPacket);
                            break;
                        case 1:
                            structSize = Marshal.SizeOf(CPacket);
                            break;
                    }

                    //make sure the binary structs on both Arduino and plugin are the same size.
                    if (rx_len != structSize || rx_len == 0)
                    {
                        rx_len = 0;
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            else
            {
                while (Port.BytesToRead > 0 && rx_array_inx <= rx_len)
                {
                    buffer[rx_array_inx++] = (byte)Port.ReadByte();
                }
                buffer[0] = id;

                if (rx_len == (rx_array_inx - 1))
                {
                    //seem to have got whole message
                    //last uint8_t is CS
                    calc_CS = rx_len;
                    for (int i = 0; i < rx_len; i++)
                    {
                        calc_CS ^= buffer[i];
                    }

                    if (calc_CS == buffer[rx_array_inx - 1])
                    {//CS good
                        rx_len = 0;
                        rx_array_inx = 1;
                        return true;
                    }
                    else
                    {
                        //failed checksum, need to clear this out anyway
                        rx_len = 0;
                        rx_array_inx = 1;
                        return false;
                    }
                }
            }

            return false;
        }

        private void HandShake()
        {
            HPacket = (HandShakePacket)ByteArrayToStructure(buffer, HPacket);

            //HPacket = ReadUsingMarshalUnsafe<HandShakePacket>(buffer);            
            Debug.Log("KSPSerialIO: Handshake received - " + HPacket.M1.ToString() + HPacket.M2.ToString() + HPacket.M3.ToString());
        }

        private void VesselControls()
        {
            CPacket = (ControlPacket)ByteArrayToStructure(buffer, CPacket);

            VControls.SAS = BitMathByte(CPacket.MainControls, 7);
            VControls.RCS = BitMathByte(CPacket.MainControls, 6);
            VControls.Lights = BitMathByte(CPacket.MainControls, 5);
            VControls.Gear = BitMathByte(CPacket.MainControls, 4);
            VControls.Brakes = BitMathByte(CPacket.MainControls, 3);
            VControls.Precision = BitMathByte(CPacket.MainControls, 2);
            VControls.Abort = BitMathByte(CPacket.MainControls, 1);
            VControls.Stage = BitMathByte(CPacket.MainControls, 0);
            VControls.Throttle = (float)CPacket.Throttle / 1000.0F;
            VControls.Pitch = (float)CPacket.Pitch / 1000.0F;
            VControls.Roll = (float)CPacket.Roll / 1000.0F;
            VControls.Yaw = (float)CPacket.Yaw / 1000.0F;
            VControls.TX = (float)CPacket.TX / 1000.0F;
            VControls.TY = (float)CPacket.TY / 1000.0F;
            VControls.TZ = (float)CPacket.TZ / 1000.0F;

            for (int j = 1; j <= 10; j++)
            {
                VControls.ControlGroup[j] = BitMathUshort(CPacket.ControlGroup, j);
            }

            ControlReceived = true;
            //Debug.Log("KSPSerialIO: ControlPacket received");
        }

        private Boolean BitMathByte(byte x, int n)
        {
            return ((x >> n) & 1) == 1;
        }

        private Boolean BitMathUshort(ushort x, int n)
        {
            return ((x >> n) & 1) == 1;
        }

        private void Unimplemented()
        {
            Debug.Log("KSPSerialIO: Packet id unimplemented");
        }

        private static void debug()
        {
            Debug.Log(Port.BytesToRead.ToString() + "BTR");
        }

        void OnDestroy()
        {
            if (KSPSerialPort.Port.IsOpen)
            {
                KSPSerialPort.Port.Close();
                Port.ReceivedEvent -= Port_ReceivedEvent;
                Debug.Log("KSPSerialIO: Port closed");
            }
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KSPSerialIO : MonoBehaviour
    {
        private double lastUpdate = 0.0f;
        public double refreshrate = 1.0f;
        public static Vessel ActiveVessel;
        IOResource TempR = new IOResource();

        private ScreenMessageStyle KSPIOScreenStyle = ScreenMessageStyle.UPPER_RIGHT;

        void Awake()
        {
            ScreenMessages.PostScreenMessage("IO awake", 10f, KSPIOScreenStyle);
            refreshrate = SettingsNStuff.refreshrate;
        }

        void Start()
        {
            if (KSPSerialPort.DisplayFound)
            {
                if (!KSPSerialPort.Port.IsOpen)
                {
                    ScreenMessages.PostScreenMessage("Starting serial port " + KSPSerialPort.Port.PortName, 10f, KSPIOScreenStyle);

                    try
                    {
                        KSPSerialPort.Port.Open();
                    }
                    catch (Exception e)
                    {
                        ScreenMessages.PostScreenMessage("Error opening serial port " + KSPSerialPort.Port.PortName, 10f, KSPIOScreenStyle);
                        ScreenMessages.PostScreenMessage(e.Message, 10f, KSPIOScreenStyle);
                    }
                }
                else
                {
                    ScreenMessages.PostScreenMessage("Using serial port " + KSPSerialPort.Port.PortName, 10f, KSPIOScreenStyle);
                }

                Thread.Sleep(200);
                ActiveVessel = FlightGlobals.ActiveVessel;
                //sync inputs at start
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.RCS, KSPSerialPort.VControls.RCS);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.SAS, KSPSerialPort.VControls.SAS);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Light, KSPSerialPort.VControls.Lights);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Gear, KSPSerialPort.VControls.Gear);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, KSPSerialPort.VControls.Brakes);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Abort, KSPSerialPort.VControls.Abort);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Stage, KSPSerialPort.VControls.Stage);

                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom01, KSPSerialPort.VControls.ControlGroup[1]);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom02, KSPSerialPort.VControls.ControlGroup[2]);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom03, KSPSerialPort.VControls.ControlGroup[3]);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom04, KSPSerialPort.VControls.ControlGroup[4]);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom05, KSPSerialPort.VControls.ControlGroup[5]);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom06, KSPSerialPort.VControls.ControlGroup[6]);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom07, KSPSerialPort.VControls.ControlGroup[7]);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom08, KSPSerialPort.VControls.ControlGroup[8]);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom09, KSPSerialPort.VControls.ControlGroup[9]);
                ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom10, KSPSerialPort.VControls.ControlGroup[10]);

                ActiveVessel.OnFlyByWire -= new FlightInputCallback(AxisInput);
                ActiveVessel.OnFlyByWire += new FlightInputCallback(AxisInput);
            }
            else
            {
                ScreenMessages.PostScreenMessage("No display found", 10f, KSPIOScreenStyle);
            }
        }

        void Update()
        {
            ActiveVessel = FlightGlobals.ActiveVessel;

            if (ActiveVessel != null)
            {

                #region outputs
                if ((Time.time - lastUpdate) > refreshrate && KSPSerialPort.Port.IsOpen)
                {

                    lastUpdate = Time.time;

                    KSPSerialPort.VData.AP = (float)ActiveVessel.orbit.ApA;
                    KSPSerialPort.VData.PE = (float)ActiveVessel.orbit.PeA;
                    KSPSerialPort.VData.SemiMajorAxis = (float)ActiveVessel.orbit.semiMajorAxis;
                    KSPSerialPort.VData.SemiMinorAxis = (float)ActiveVessel.orbit.semiMinorAxis;
                    KSPSerialPort.VData.e = (float)ActiveVessel.orbit.eccentricity;
                    KSPSerialPort.VData.inc = (float)ActiveVessel.orbit.inclination;
                    KSPSerialPort.VData.VVI = (float)ActiveVessel.verticalSpeed;
                    KSPSerialPort.VData.G = (float)ActiveVessel.geeForce;
                    KSPSerialPort.VData.TAp = (int)Math.Round(ActiveVessel.orbit.timeToAp);
                    KSPSerialPort.VData.TPe = (int)Math.Round(ActiveVessel.orbit.timeToPe);
                    KSPSerialPort.VData.Density = (float)ActiveVessel.atmDensity;
                    KSPSerialPort.VData.TrueAnomaly = (float)ActiveVessel.orbit.trueAnomaly;
                    KSPSerialPort.VData.period = (int)Math.Round(ActiveVessel.orbit.period);


                    double ASL = ActiveVessel.mainBody.GetAltitude(ActiveVessel.CoM);
                    double AGL = (ASL - ActiveVessel.terrainAltitude);

                    if (AGL < ASL)
                        KSPSerialPort.VData.RAlt = (float)AGL;
                    else
                        KSPSerialPort.VData.RAlt = (float)ASL;

                    KSPSerialPort.VData.Alt = (float)ASL;
                    KSPSerialPort.VData.Vsurf = (float)ActiveVessel.srfSpeed;
                    KSPSerialPort.VData.Lat = (float)ActiveVessel.latitude;
                    KSPSerialPort.VData.Lon = (float)ActiveVessel.longitude;

                    TempR = GetResourceTotal(ActiveVessel, "LiquidFuel");
                    KSPSerialPort.VData.LiquidFuelTot = TempR.Max;
                    KSPSerialPort.VData.LiquidFuel = TempR.Current;
                    TempR = GetResourceTotal(ActiveVessel, "Oxidizer");
                    KSPSerialPort.VData.OxidizerTot = TempR.Max;
                    KSPSerialPort.VData.Oxidizer = TempR.Current;
                    TempR = GetResourceTotal(ActiveVessel, "ElectricCharge");
                    KSPSerialPort.VData.EChargeTot = TempR.Max;
                    KSPSerialPort.VData.ECharge = TempR.Current;
                    TempR = GetResourceTotal(ActiveVessel, "MonoPropellant");
                    KSPSerialPort.VData.MonoPropTot = TempR.Max;
                    KSPSerialPort.VData.MonoProp = TempR.Current;
                    TempR = GetResourceTotal(ActiveVessel, "IntakeAir");
                    KSPSerialPort.VData.IntakeAirTot = TempR.Max;
                    KSPSerialPort.VData.IntakeAir = TempR.Current;
                    TempR = GetResourceTotal(ActiveVessel, "SolidFuel");
                    KSPSerialPort.VData.SolidFuelTot = TempR.Max;
                    KSPSerialPort.VData.SolidFuel = TempR.Current;
                    TempR = GetResourceTotal(ActiveVessel, "XenonGas");
                    KSPSerialPort.VData.XenonGasTot = TempR.Max;
                    KSPSerialPort.VData.XenonGas = TempR.Current;

                    //Debug.Log("KSPSerialIO: Throttle = " + KSPSerialPort.CPacket.Throttle.ToString());
                    //ScreenMessages.PostScreenMessage(KSPSerialPort.VData.Fuelp.ToString());
                    //ScreenMessages.PostScreenMessage(KSPSerialPort.VData.RAlt.ToString());
                    //KSPSerialPort.Port.WriteLine("Success!");

                    KSPSerialPort.sendPacket(KSPSerialPort.VData);
                } //end refresh
                #endregion
                #region inputs
                if (KSPSerialPort.ControlReceived)
                {/*
                  * 
                    ScreenMessages.PostScreenMessage("SAS: " + KSPSerialPort.VControls.SAS.ToString() +
                    ", RCS: " + KSPSerialPort.VControls.RCS.ToString() +
                    ", Lights: " + KSPSerialPort.VControls.Lights.ToString() +
                    ", Gear: " + KSPSerialPort.VControls.Gear.ToString() +
                    ", Brakes: " + KSPSerialPort.VControls.Brakes.ToString() +
                    ", Precision: " + KSPSerialPort.VControls.Precision.ToString() +
                    ", Abort: " + KSPSerialPort.VControls.Abort.ToString() +
                    ", Stage: " + KSPSerialPort.VControls.Stage.ToString(), 10f, KSPIOScreenStyle);
                    */


                    if (Math.Abs(KSPSerialPort.VControls.Pitch) > SettingsNStuff.SASTol ||
                        Math.Abs(KSPSerialPort.VControls.Roll) > SettingsNStuff.SASTol ||
                        Math.Abs(KSPSerialPort.VControls.Yaw) > SettingsNStuff.SASTol)
                    {
                        ActiveVessel.vesselSAS.ManualOverride(true);
                        //KSPSerialPort.VControlsOld.Pitch = KSPSerialPort.VControls.Pitch;
                        //KSPSerialPort.VControlsOld.Roll = KSPSerialPort.VControls.Roll;
                        //KSPSerialPort.VControlsOld.Yaw = KSPSerialPort.VControls.Yaw;
                    }
                    else
                    {
                        ActiveVessel.vesselSAS.ManualOverride(false);
                    }

                    //if (FlightInputHandler.RCSLock != KSPSerialPort.VControls.RCS)
                    if (KSPSerialPort.VControls.RCS != KSPSerialPort.VControlsOld.RCS)
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.RCS, KSPSerialPort.VControls.RCS);
                        KSPSerialPort.VControlsOld.RCS = KSPSerialPort.VControls.RCS;
                        //ScreenMessages.PostScreenMessage("RCS: " + KSPSerialPort.VControls.RCS.ToString(), 10f, KSPIOScreenStyle);
                    }

                    //if (ActiveVessel.ctrlState.killRot != KSPSerialPort.VControls.SAS)
                    if (KSPSerialPort.VControls.SAS != KSPSerialPort.VControlsOld.SAS)
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.SAS, KSPSerialPort.VControls.SAS);
                        KSPSerialPort.VControlsOld.SAS = KSPSerialPort.VControls.SAS;
                        //ScreenMessages.PostScreenMessage("SAS: " + KSPSerialPort.VControls.SAS.ToString(), 10f, KSPIOScreenStyle);
                    }

                    if (KSPSerialPort.VControls.Lights != KSPSerialPort.VControlsOld.Lights)
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Light, KSPSerialPort.VControls.Lights);
                        KSPSerialPort.VControlsOld.Lights = KSPSerialPort.VControls.Lights;
                    }

                    if (KSPSerialPort.VControls.Gear != KSPSerialPort.VControlsOld.Gear)
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Gear, KSPSerialPort.VControls.Gear);
                        KSPSerialPort.VControlsOld.Gear = KSPSerialPort.VControls.Gear;
                    }

                    if (KSPSerialPort.VControls.Brakes != KSPSerialPort.VControlsOld.Brakes)
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, KSPSerialPort.VControls.Brakes);
                        KSPSerialPort.VControlsOld.Brakes = KSPSerialPort.VControls.Brakes;
                    }

                    if (KSPSerialPort.VControls.Abort != KSPSerialPort.VControlsOld.Abort)
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Abort, KSPSerialPort.VControls.Abort);
                        KSPSerialPort.VControlsOld.Abort = KSPSerialPort.VControls.Abort;
                    }

                    if (KSPSerialPort.VControls.Stage != KSPSerialPort.VControlsOld.Stage)
                    {
                        if (KSPSerialPort.VControls.Stage)
                            Staging.ActivateNextStage();

                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Stage, KSPSerialPort.VControls.Stage);
                        KSPSerialPort.VControlsOld.Stage = KSPSerialPort.VControls.Stage;
                    }

                    //================ control groups

                    if (KSPSerialPort.VControls.ControlGroup[1] != KSPSerialPort.VControlsOld.ControlGroup[1])
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom01, KSPSerialPort.VControls.ControlGroup[1]);
                        KSPSerialPort.VControlsOld.ControlGroup[1] = KSPSerialPort.VControls.ControlGroup[1];
                    }

                    if (KSPSerialPort.VControls.ControlGroup[2] != KSPSerialPort.VControlsOld.ControlGroup[2])
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom02, KSPSerialPort.VControls.ControlGroup[2]);
                        KSPSerialPort.VControlsOld.ControlGroup[2] = KSPSerialPort.VControls.ControlGroup[2];
                    }

                    if (KSPSerialPort.VControls.ControlGroup[3] != KSPSerialPort.VControlsOld.ControlGroup[3])
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom03, KSPSerialPort.VControls.ControlGroup[3]);
                        KSPSerialPort.VControlsOld.ControlGroup[3] = KSPSerialPort.VControls.ControlGroup[3];
                    }

                    if (KSPSerialPort.VControls.ControlGroup[4] != KSPSerialPort.VControlsOld.ControlGroup[4])
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom04, KSPSerialPort.VControls.ControlGroup[4]);
                        KSPSerialPort.VControlsOld.ControlGroup[4] = KSPSerialPort.VControls.ControlGroup[4];
                    }

                    if (KSPSerialPort.VControls.ControlGroup[5] != KSPSerialPort.VControlsOld.ControlGroup[5])
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom05, KSPSerialPort.VControls.ControlGroup[5]);
                        KSPSerialPort.VControlsOld.ControlGroup[5] = KSPSerialPort.VControls.ControlGroup[5];
                    }

                    if (KSPSerialPort.VControls.ControlGroup[6] != KSPSerialPort.VControlsOld.ControlGroup[6])
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom06, KSPSerialPort.VControls.ControlGroup[6]);
                        KSPSerialPort.VControlsOld.ControlGroup[6] = KSPSerialPort.VControls.ControlGroup[6];
                    }

                    if (KSPSerialPort.VControls.ControlGroup[7] != KSPSerialPort.VControlsOld.ControlGroup[7])
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom07, KSPSerialPort.VControls.ControlGroup[7]);
                        KSPSerialPort.VControlsOld.ControlGroup[7] = KSPSerialPort.VControls.ControlGroup[7];
                    }

                    if (KSPSerialPort.VControls.ControlGroup[8] != KSPSerialPort.VControlsOld.ControlGroup[8])
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom08, KSPSerialPort.VControls.ControlGroup[8]);
                        KSPSerialPort.VControlsOld.ControlGroup[8] = KSPSerialPort.VControls.ControlGroup[8];
                    }

                    if (KSPSerialPort.VControls.ControlGroup[9] != KSPSerialPort.VControlsOld.ControlGroup[9])
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom09, KSPSerialPort.VControls.ControlGroup[9]);
                        KSPSerialPort.VControlsOld.ControlGroup[9] = KSPSerialPort.VControls.ControlGroup[9];
                    }

                    if (KSPSerialPort.VControls.ControlGroup[10] != KSPSerialPort.VControlsOld.ControlGroup[10])
                    {
                        ActiveVessel.ActionGroups.SetGroup(KSPActionGroup.Custom10, KSPSerialPort.VControls.ControlGroup[10]);
                        KSPSerialPort.VControlsOld.ControlGroup[10] = KSPSerialPort.VControls.ControlGroup[10];
                    } 

                    KSPSerialPort.ControlReceived = false;
                } //end ControlReceived
                #endregion
            }//end if null
            else
            {
                ActiveVessel.OnFlyByWire -= new FlightInputCallback(AxisInput);
            }
        }

        #region utilities
        private IOResource GetResourceTotal(Vessel V, string resourceName)
        {
            IOResource R = new IOResource();

            foreach (Part p in ActiveVessel.parts)
            {
                foreach (PartResource pr in p.Resources)
                {
                    if (pr.resourceName.Equals(resourceName))
                    {
                        R.Current += (float)pr.amount;
                        R.Max += (float)pr.maxAmount;
                        break;
                    }
                }
            }

            if (R.Max == 0)
                R.Current = 0;

            return R;
        }

        private void AxisInput(FlightCtrlState s)
        {

            if (SettingsNStuff.ThrottleEnable)
                s.mainThrottle = KSPSerialPort.VControls.Throttle;

            if (SettingsNStuff.PitchEnable)
                s.pitch = KSPSerialPort.VControls.Pitch;

            if (SettingsNStuff.RollEnable)
                s.roll = KSPSerialPort.VControls.Roll;

            if (SettingsNStuff.YawEnable) 
                s.yaw = KSPSerialPort.VControls.Yaw;

            if (SettingsNStuff.TXEnable)
                s.X = KSPSerialPort.VControls.TX;

            if (SettingsNStuff.TYEnable)
                s.Y = KSPSerialPort.VControls.TY;

            if (SettingsNStuff.TZEnable)
                s.Z = KSPSerialPort.VControls.TZ;

        }
        #endregion

        void FixedUpdate()
        {
        }

        void OnDestroy()
        {
            if (KSPSerialPort.Port.IsOpen)
            {
                KSPSerialPort.Port.Close();
                ScreenMessages.PostScreenMessage("Port closed", 10f, KSPIOScreenStyle);
            }

            ActiveVessel.OnFlyByWire -= new FlightInputCallback(AxisInput);
        }
    }
}
