﻿/*
 * This file is part of VitalSignsCapture v1.005.
 * Copyright (C) 2012-2016 John George K., xeonfusion@users.sourceforge.net

    VitalSignsCapture is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    VitalSignsCapture is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with VitalSignsCapture.  If not, see <http://www.gnu.org/licenses/>.*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO.Ports;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Globalization;

namespace VSCapture
{
    public sealed class DSerialPort:SerialPort
    {
        // Main Datex Record variables
        private datex_record_req_type request_ptr = new datex_record_req_type();
        public List<datex_record_type> RecordList = new List<datex_record_type>();
        public List<byte[]> FrameList = new List<byte[]>();
        private int DPortBufSize;
        public byte[] DPort_rxbuf;
		private datex_tx_type DPort_txbuf = new datex_tx_type();

        private bool m_fstart = true;
        private bool m_storestart = false;
        private bool m_storeend = false;
        private bool m_bitshiftnext = false;
        private List<byte> m_bList = new List<byte>();
        private StringBuilder m_strBuilder = new StringBuilder();

		private bool m_transmissionstart = true;

		//Create a singleton serialport subclass
		private static volatile DSerialPort DPort = null;
		
		public static DSerialPort getInstance
		{
			
			get
			{
				if (DPort == null)
				{
					lock (typeof(DSerialPort))
						if (DPort == null)
					{
						DPort = new DSerialPort();
					}
					
				} return DPort;
			}
			
		}

        public DSerialPort ()
        {
            DPort = this;
            
            DPortBufSize = 4096;
            DPort_rxbuf = new byte[DPortBufSize];
            
			if (OSIsUnix())
			DPort.PortName = "/dev/ttyUSB0"; //default Unix port
			else DPort.PortName = "COM1"; //default Windows port
			
            DPort.BaudRate = 19200;
            DPort.Parity = Parity.Even;
            DPort.DataBits = 8;
            DPort.StopBits = StopBits.One;
            
            DPort.Handshake = Handshake.RequestToSend;

            // Set the read/write timeouts
            DPort.ReadTimeout = 600000;
            DPort.WriteTimeout = 600000;
            
            //ASCII Encoding in C# is only 7bit so
            DPort.Encoding = Encoding.GetEncoding("ISO-8859-1");

            
        }
		
        public void WriteBuffer(byte[] txbuf)   
        {
			byte []framebyte = {DataConstants.CTRLCHAR, (DataConstants.FRAMECHAR & DataConstants.BIT5COMPL),0};
			byte []ctrlbyte = {DataConstants.CTRLCHAR, (DataConstants.CTRLCHAR & DataConstants.BIT5COMPL),0};
			
			byte check_sum = 0x00;
			byte b1 = 0x00;
			byte b2 = 0x00;
			
			int txbuflen = (txbuf.GetLength (0) +1);
			//Create write packet buffer
			byte[] temptxbuff = new byte[txbuflen];
			//Clear the buffer
			for (int j = 0; j < txbuflen; j++)
			{
				temptxbuff[j] = 0;
			}

			// Send start frame characters
			temptxbuff[0] = DataConstants.FRAMECHAR;

			int i = 1;
			
			foreach (byte b in txbuf)
			{
				switch (b)
				{
				case DataConstants.FRAMECHAR:
					temptxbuff[i] = framebyte[0];
					temptxbuff[i + 1] = framebyte[1];
					i +=2;
					b1 += framebyte[0];
					b1 += framebyte[1];
					check_sum += b1;
					break;
				case DataConstants.CTRLCHAR:
					temptxbuff[i] = ctrlbyte[0];
					temptxbuff[i + 1] = ctrlbyte[1];
					i +=2;
					b2 += ctrlbyte[0];
					b2 += ctrlbyte[1];
					check_sum += b2;
					break;
				default:
					temptxbuff[i] = b;
					i++;
					check_sum += b;
					break;
				};
			}
			
			int buflen = i;
			byte[]finaltxbuff = new byte[buflen+2];
			
			for (int j = 0; j < buflen; j++)
			{
				finaltxbuff[j] = temptxbuff[j];
			}
			
			// Send Checksum
			finaltxbuff[buflen] = check_sum;
			// Send stop frame characters
			finaltxbuff[buflen+1] = DataConstants.FRAMECHAR;
			
			try
			{
				DPort.Write(finaltxbuff, 0, buflen + 2);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error opening/writing to serial port :: " + ex.Message, "Error!");
			}

        }

        public void ClearReadBuffer()
        {
            //Clear the buffer
            for (int i = 0; i < DPortBufSize; i++)
            {
                DPort_rxbuf[i] = 0;
            }
        }

        
        public int ReadBuffer()
        {
            int bytesreadtotal = 0;
            
            try
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(),"AS3Rawoutput1.raw");
				                
                int lenread = 0;
                
                do
                {
                    ClearReadBuffer();
                    lenread = DPort.Read(DPort_rxbuf, 0, DPortBufSize);
                    
                    byte[] copyarray = new byte[lenread];

                    for (int i = 0; i < lenread; i++)
                    {
                        copyarray[i] = DPort_rxbuf[i];
                        CreateFrameListFromByte(copyarray[i]);
                    }

                    ByteArrayToFile(path, copyarray, copyarray.GetLength(0));
                    bytesreadtotal += lenread;
                    if (FrameList.Count > 0)
                    {
                        CreateRecordList();
                        ReadSubRecords();
                       
                        FrameList.RemoveRange(0, FrameList.Count);
                        RecordList.RemoveRange(0, RecordList.Count);
                        //m_bList.RemoveRange(0, m_bList.Count);
                    }
                    
                }
                while (DPort.BytesToRead !=0);
                
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error opening/writing to serial port :: " + ex.Message, "Error!");
            }


            return bytesreadtotal;
	    }

        public void CreateFrameListFromByte(byte b)
        {
			if (b == DataConstants.FRAMECHAR && m_fstart)
			{
				m_fstart = false;
				m_storestart = true;
			}
			else if (b == DataConstants.FRAMECHAR && (m_fstart == false))
			{
				m_fstart = true;
				m_storeend = true;
				m_storestart = false;
				if (b != DataConstants.FRAMECHAR) m_bList.Add(b);
			}
			
			if (m_storestart == true)
			{
				if (b == DataConstants.CTRLCHAR)
					m_bitshiftnext = true;
				else
				{
					if (m_bitshiftnext == true)
					{
						b |= DataConstants.BIT5;
						m_bitshiftnext = false;
						m_bList.Add(b);
					}
					else if (b != DataConstants.FRAMECHAR) m_bList.Add(b);
				}
				
			}
			else if (m_storeend == true)
			{
				int framelen = m_bList.Count();
				if (framelen != 0)
				{
					byte[] bArray = new byte[framelen];
					bArray = m_bList.ToArray();
					//Calculate checksum
					byte checksum = 0x00;
					for (int j = 0; j < (framelen - 1); j++)
					{
						checksum += bArray[j];
					}
					if (checksum == bArray[framelen - 1])
					{
						FrameList.Add(bArray);
					}
					m_bList.Clear();
					m_storeend = false;
				}
				else
				{
					m_storestart = true;
					m_storeend = false;
					m_fstart = false;
				}
				
			}
        }
        

        public void CreateRecordList()
        {
			//Read record from Framelist();
			
			int recorddatasize = 0;
			byte[] fullrecord = new byte[1490];
			
			foreach(byte[] fArray in FrameList)
			{
				datex_record_type record_dtx = new datex_record_type();
				
				for(int i=0;i<fullrecord.GetLength(0);i++)
				{
					fullrecord[i] = 0x00;
				}
				
				recorddatasize = fArray.GetLength(0);
				
				for (int n = 0; n < (fArray.GetLength(0)) && recorddatasize < 1490; n++)
				{
					fullrecord[n] = fArray[n];
				}
				
				/*int uMemlen = recorddatasize;
                IntPtr ptr = Marshal.AllocHGlobal(uMemlen);
                Marshal.Copy(fullrecord, 0, ptr, uMemlen);
                Marshal.PtrToStructure(ptr, record_dtx);
                RecordList.Add(record_dtx);
                Marshal.FreeHGlobal(ptr);*/

				GCHandle handle2 = GCHandle.Alloc(fullrecord, GCHandleType.Pinned);
				Marshal.PtrToStructure(handle2.AddrOfPinnedObject(), record_dtx);
				
				RecordList.Add(record_dtx);
				handle2.Free();
				
			}

       }

		public void RequestTransfer(byte Trtype, short Interval, byte DRIlevel)
        {
            //Set Record Header
            request_ptr.hdr.r_len = 49; //size of hdr + phdb type
            request_ptr.hdr.r_dri_level = DRIlevel;
            request_ptr.hdr.r_time = 0;
            request_ptr.hdr.r_maintype = DataConstants.DRI_MT_PHDB;


            request_ptr.hdr.sr_offset1 = 0;
            request_ptr.hdr.sr_type1 = 0; // Physiological data request
            request_ptr.hdr.sr_offset2 = 0;
            request_ptr.hdr.sr_type2 = 0xFF; // Last subrecord

            // Request transmission subrecord
	        request_ptr.phdbr.phdb_rcrd_type = Trtype;
            request_ptr.phdbr.tx_interval	= Interval;
            if (Interval !=0) request_ptr.phdbr.phdb_class_bf =
                 DataConstants.DRI_PHDBCL_REQ_BASIC_MASK | DataConstants.DRI_PHDBCL_REQ_EXT1_MASK |
                 DataConstants.DRI_PHDBCL_REQ_EXT2_MASK | DataConstants.DRI_PHDBCL_REQ_EXT3_MASK;
            else request_ptr.phdbr.phdb_class_bf = 0x0000;

			//Get pointer to structure in memory
			
			IntPtr uMemoryValue = IntPtr.Zero;
			
			try
			{
				uMemoryValue = Marshal.AllocHGlobal(49);
				Marshal.StructureToPtr(request_ptr, uMemoryValue, true);
				Marshal.PtrToStructure(uMemoryValue, DPort_txbuf);
				WriteBuffer(DPort_txbuf.data);
			}
			finally
			{
				if (uMemoryValue != IntPtr.Zero)
				{
					Marshal.FreeHGlobal(uMemoryValue);
				}
			}

			m_transmissionstart = true;

        }

        public void StopTransfer()
        {
            //RequestTransfer(DataConstants.DRI_PH_60S_TREND, 0);
			RequestTransfer(DataConstants.DRI_PH_DISPL, 0, DataConstants.DRI_LEVEL_2005);
			RequestTransfer(DataConstants.DRI_PH_DISPL, 0, DataConstants.DRI_LEVEL_2003);
			RequestTransfer(DataConstants.DRI_PH_DISPL, 0, DataConstants.DRI_LEVEL_2001);
        }

        public bool ByteArrayToFile(string _FileName, byte[] _ByteArray, int nWriteLength) 
        {
            try 
            { 
                // Open file for reading. 
                FileStream _FileStream = new FileStream(_FileName, FileMode.Append, FileAccess.Write); 
        
                // Writes a block of bytes to this stream using data from a byte array
                _FileStream.Write(_ByteArray, 0, nWriteLength);
        
                // close file stream. 
                _FileStream.Close();
                 return true;
            }
    
            catch (Exception _Exception) 
            { 
                // Error. 
                Console.WriteLine("Exception caught in process: {0}", _Exception.ToString()); 
            } 
            // error occured, return false. 
            return false;
        }

        
        public void ReadSubRecords()
        {
            foreach (datex_record_type dx_record in RecordList)
            {

                short dxrecordmaintype = dx_record.hdr.r_maintype;
               
                if (dxrecordmaintype == DataConstants.DRI_MT_PHDB)
                {

                    short[] sroffArray = { dx_record.hdr.sr_offset1, dx_record.hdr.sr_offset2, dx_record.hdr.sr_offset3, dx_record.hdr.sr_offset4, dx_record.hdr.sr_offset5, dx_record.hdr.sr_offset6, dx_record.hdr.sr_offset7, dx_record.hdr.sr_offset8 };
                    byte[] srtypeArray = { dx_record.hdr.sr_type1, dx_record.hdr.sr_type2, dx_record.hdr.sr_type3, dx_record.hdr.sr_type4, dx_record.hdr.sr_type5, dx_record.hdr.sr_type6, dx_record.hdr.sr_type7, dx_record.hdr.sr_type8 };

                    uint unixtime = dx_record.hdr.r_time;
                    dri_phdb phdata_ptr = new dri_phdb();

                    WriteNumericHeaders();

                    for (int i = 0; i < 8 && (srtypeArray[i] != 0xFF); i++)
                    {
                        //if (srtypeArray[i] == DataConstants.DRI_PH_DISPL && srtypeArray[i] !=0xFF)
                        {
                            int offset = (int)sroffArray[i];

                            byte[] buffer = new byte[270];
                            for (int j = 0; j < 270; j++)
                            {
                                buffer[j] = dx_record.data[4 + j + offset];
                            }

                            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

                            /*int uMemlen = buffer.GetLength(0);
                            IntPtr ptr = Marshal.AllocHGlobal(uMemlen);
                            Marshal.Copy(buffer, 0, ptr, uMemlen);*/


                            switch (i)
                            {
                                case 0:
                                    Marshal.PtrToStructure(handle.AddrOfPinnedObject(), phdata_ptr.basic);
                                    //Marshal.PtrToStructure(ptr, phdata_ptr.basic);
                                    break;
                                case 1:
                                    Marshal.PtrToStructure(handle.AddrOfPinnedObject(), phdata_ptr.ext1);
                                    //Marshal.PtrToStructure(ptr, phdata_ptr.ext1);
                                    break;
                                case 2:
                                    Marshal.PtrToStructure(handle.AddrOfPinnedObject(), phdata_ptr.ext2);
                                    //Marshal.PtrToStructure(ptr, phdata_ptr.ext2);
                                    break;
                                case 3:
                                    Marshal.PtrToStructure(handle.AddrOfPinnedObject(), phdata_ptr.ext3);
                                    //Marshal.PtrToStructure(ptr, phdata_ptr.ext3);
                                    break;
                            }

                            handle.Free();
                            //Marshal.FreeHGlobal(ptr);
                        }
                    }
                    // Unix timestamp is seconds past epoch 
                    DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0);
                    //dtDateTime = dtDateTime.AddSeconds(unixtime).ToLocalTime();
                    dtDateTime = dtDateTime.AddSeconds(unixtime);

                    Console.WriteLine();
                    Console.WriteLine("Time:{0}", dtDateTime.ToString());
                    m_strBuilder.Append(dtDateTime.ToShortDateString());
                    m_strBuilder.Append(',');
                    m_strBuilder.Append(dtDateTime.ToLongTimeString());
                    m_strBuilder.Append(',');

                    ShowBasicSubRecord(phdata_ptr);
                    ShowExt1and2SubRecord(phdata_ptr);
                }
            }
            
            
        }

		public void WriteNumericHeaders()
		{
			if (m_transmissionstart)
			{
				m_strBuilder.AppendLine("VitalSignsCapture v1.005");
				m_strBuilder.AppendLine("Datex AS3 Monitor");

				m_strBuilder.Append("Date,Time,Heart Rate(/min),Systolic BP(mmHg),Diastolic BP(mmHg),Mean BP(mmHg),SpO2(%),ETCO2(mmHg),CO2 FI,ETO2(mmHg)");
				//m_strBuilder.Append("AA ET, AA FI, AA MAC SUM, AA, O2 FI, N2O FI, N2O ET, RR, T1, T2, P1 HR, P1 Sys, P1 Dia, P1 Mean, P2 HR, P2 Sys, P2 Dia, P2Mean, PPeak, PPlat, TV Exp, ST II(mm),ST V5(mm),ST aVL(mm),");
				m_strBuilder.Append ("AA ET, AA FI, AA MAC SUM, AA, O2 FI, N2O FI, N2O ET, RR, T1, T2, P1 HR, P1 Sys, P1 Dia, P1 Mean, P2 HR, P2 Sys, P2 Dia, P2Mean,");
				m_strBuilder.Append("PPeak, PPlat, TV Exp, TV Insp, Peep, MV Exp, Compliance, RR,");
				m_strBuilder.Append("ST II(mm),ST V5(mm),ST aVL(mm),");
				m_strBuilder.AppendLine("SE, RE, ENTROPY BSR, BIS, BIS BSR, BIS EMG, BIS SQI");

			}

		}

		public void ShowBasicSubRecord(dri_phdb driSR)
        {
            short so1 = driSR.basic.ecg.hr;
            short so2 = driSR.basic.nibp.sys;
            short so3 = driSR.basic.nibp.dia;
            short so4 = driSR.basic.nibp.mean;
            short so5 = driSR.basic.SpO2.SpO2;
            short so6 = driSR.basic.co2.et;

            string s1 = ValidateDataFormatString(so1, 1, true);
            ValidateAddData(so1,1,true);
                        
            string s2 = ValidateDataFormatString(so2, 0.01, true);
            ValidateAddData(so2, 0.01, true);
            
            string s3 = ValidateDataFormatString(so3, 0.01, true);
            ValidateAddData(so3, 0.01, true);

            string s4 = ValidateDataFormatString(so4, 0.01, true);
            ValidateAddData(so4, 0.01, true);

            string s5 = ValidateDataFormatString(so5, 0.01, true);
            ValidateAddData(so5, 0.01, true);

            double et = (so6 * driSR.basic.co2.amb_press);
            ValidateAddData(et, 0.00001,true);
            string s6 = ValidateDataFormatString(et, 0.00001, true);
            
			// MASSIVE SPECIALK HACK
			short wtf1 = driSR.basic.co2.fi;
			double fraction_inspired_co2 = wtf1 * driSR.basic.co2.amb_press;
			ValidateAddData (fraction_inspired_co2, 0.00001, true);

			short wtf2 = driSR.basic.o2.et;
			double end_tidal_o2 = wtf2 //02 isnt multiplied by ambient pressure
			ValidateAddData (end_tidal_o2, 0.00001, true);

			// EOHACK

            short so7 = driSR.basic.aa.et;
            short so8 = driSR.basic.aa.fi;
            short so9 = driSR.basic.aa.mac_sum;
            ushort so10 = driSR.basic.aa.hdr.label_info;

            ValidateAddData(so7, 0.01, false);
            ValidateAddData(so8, 0.01, false);
            ValidateAddData(so9, 0.01, false);

            string s10 = "";

            switch (so10)
            {
                case 0:
                    s10 = "Unknown";
                    break;
                case 1:
                    s10 = "None";
                    break;
                case 2:
                    s10 = "HAL";
                    break;
                case 3:
                    s10 = "ENF";
                    break;
                case 4:
                    s10 = "ISO";
                    break;
                case 5:
                    s10 = "DES";
                    break;
                case 6:
                    s10 = "SEV";
                    break;
            }

            m_strBuilder.Append(s10);
            m_strBuilder.Append(',');

            double so11 = driSR.basic.o2.fi;
            double so12 = driSR.basic.n2o.fi;
            double so13 = driSR.basic.n2o.et;
            double so14 = driSR.basic.co2.rr;
            double so15 = driSR.basic.t1.temp;
            double so16 = driSR.basic.t2.temp;

            double so17 = driSR.basic.p1.hr;
            double so18 = driSR.basic.p1.sys;
            double so19 = driSR.basic.p1.dia;
            double so20 = driSR.basic.p1.mean;
            double so21 = driSR.basic.p2.hr;
            double so22 = driSR.basic.p2.sys;
            double so23 = driSR.basic.p2.dia;
            double so24 = driSR.basic.p2.mean;
            
            double so25 = driSR.basic.flow_vol.ppeak;
            double so26 = driSR.basic.flow_vol.pplat;
            double so27 = driSR.basic.flow_vol.tv_exp;
			double so28 = driSR.basic.flow_vol.tv_insp;
			double so29 = driSR.basic.flow_vol.peep;
			double so30 = driSR.basic.flow_vol.mv_exp;
			double so31 = driSR.basic.flow_vol.compliance;
			double so32 = driSR.basic.flow_vol.rr;


            ValidateAddData(so11, 0.01, false);
            ValidateAddData(so12, 0.01, false);
            ValidateAddData(so13, 0.01, false);
            ValidateAddData(so14, 1, false);
            ValidateAddData(so15, 0.01, false);
            ValidateAddData(so16, 0.01, false);
            
            
            ValidateAddData(so17, 1, true);
            ValidateAddData(so18, 0.01, true);
            ValidateAddData(so19, 0.01, true);
            ValidateAddData(so20, 0.01, true);
            ValidateAddData(so21, 1, true);
            ValidateAddData(so22, 0.01, true);
            ValidateAddData(so23, 0.01, true);
            ValidateAddData(so24, 0.01, true);
                        
            
            ValidateAddData(so25, 0.01, true);
            ValidateAddData(so26, 0.01, true);
            ValidateAddData(so27,0.1, true);
			ValidateAddData(so28, 0.1, true);
			ValidateAddData(so29, 0.01, true);
			ValidateAddData(so30, 0.01, false);
			ValidateAddData(so31, 0.01, true);
			ValidateAddData(so32, 1, true);




            string s9 = ValidateDataFormatString(so9, 0.01, false);
            string s15 = ValidateDataFormatString(so15, 0.01, false);
            string s16 = ValidateDataFormatString(so16, 0.01, false);
            
            string s18 = ValidateDataFormatString(so18, 0.01, true);
            string s19 = ValidateDataFormatString(so19, 0.01, true);
            string s20 = ValidateDataFormatString(so20, 0.01, true);
            
            string s22 = ValidateDataFormatString(so22, 0.01, true);
            string s23 = ValidateDataFormatString(so23, 0.01, true);
            string s24 = ValidateDataFormatString(so24, 0.01, true);

            Console.WriteLine("ECG HR {0:d}/min NIBP {1:d}/{2:d}({3:d})mmHg SpO2 {4:d}% ETCO2 {5:d}mmHg", s1, s2, s3, s4, s5,s6);
            Console.WriteLine("IBP1 {0:d}/{1:d}({2:d})mmHg IBP2 {3:d}/{4:d}({5:d})mmHg MAC {6} T1 {7}°C T2 {8}°C", s18, s19, s20, s22, s23, s24, s9, s15, s16);

			m_transmissionstart = false;
        }

        public void ShowExt1and2SubRecord(dri_phdb driSR)
        {
            short so1 = driSR.ext1.ecg12.stII;
            short so2 = driSR.ext1.ecg12.stV5;
            short so3 = driSR.ext1.ecg12.stAVL;

            string pathcsv = Path.Combine(Directory.GetCurrentDirectory(),"AS3ExportData.csv");
            
            ValidateAddData(so1, 0.01,false);
            string s1 = ValidateDataFormatString(so1, 0.01, false);
            
            ValidateAddData(so2, 0.01, false);
            string s2 = ValidateDataFormatString(so2, 0.01, false);

            ValidateAddData(so3, 0.01, false);
            string s3 = ValidateDataFormatString(so3, 0.01, false);

            short so4 = driSR.ext2.ent.eeg_ent;
            short so5 = driSR.ext2.ent.emg_ent;
            short so6 = driSR.ext2.ent.bsr_ent;
            short so7 = driSR.ext2.eeg_bis.bis;
            short so8 = driSR.ext2.eeg_bis.sr_val;
            short so9 = driSR.ext2.eeg_bis.emg_val;
            short so10 = driSR.ext2.eeg_bis.sqi_val;

            ValidateAddData(so4, 1,true);
            ValidateAddData(so5, 1, true);
            ValidateAddData(so6, 1, true);
            ValidateAddData(so7, 1, true);
            ValidateAddData(so8, 1, true);
            ValidateAddData(so9, 1, true);
            ValidateAddData(so10, 1, true);
            
            ExportToCSVFile(pathcsv);

            Console.WriteLine("ST II {0:0.0}mm ST V5 {1:0.0}mm ST aVL {2:0.0}mm", s1, s2, s3);

            //Clear Stringbuilder member last
            m_strBuilder.Clear();

			m_transmissionstart = false;
        }

        
        public bool ValidateAddData(object value, double decimalshift, bool rounddata)
        {
            int val = Convert.ToInt32(value);
            double dval = (Convert.ToDouble(value, CultureInfo.InvariantCulture))*decimalshift;
            if (rounddata) dval = Math.Round(dval);

            string str = dval.ToString();
            

            if (val < DataConstants.DATA_INVALID_LIMIT)
            {
                str = "-";
                m_strBuilder.Append(str);
                m_strBuilder.Append(',');
                return false;
            }
            
            m_strBuilder.Append(str);
            m_strBuilder.Append(',');
            return true;
        }

        public string ValidateDataFormatString(object value, double decimalshift, bool rounddata)
        {
            int val = Convert.ToInt32(value);
            double dval = (Convert.ToDouble(value, CultureInfo.InvariantCulture))*decimalshift;
            if (rounddata) dval = Math.Round(dval);

            string str = dval.ToString();


            if (val < DataConstants.DATA_INVALID_LIMIT)
            {
                str = "-";
            }

            return str;
        }

        public void ExportToCSVFile(string _FileName)
        {
            try
            {
                // Open file for reading. 
                StreamWriter wrStream = new StreamWriter(_FileName, true, Encoding.UTF8);

                wrStream.WriteLine(m_strBuilder);
                m_strBuilder.Clear();
                
                // close file stream. 
                wrStream.Close();
                
            }

            catch (Exception _Exception)
            {
                // Error. 
                Console.WriteLine("Exception caught in process: {0}", _Exception.ToString());
            }
            
        }
		
		public bool OSIsUnix()
		{
			int p = (int) Environment.OSVersion.Platform;
        	if ((p == 4) || (p == 6) || (p == 128)) return true;
			else return false;
				
		}
    }

}
