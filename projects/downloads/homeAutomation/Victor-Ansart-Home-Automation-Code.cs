using System;
using System.Net;
using System.Net.Sockets;
//using System.Text;  //for SD card? try removing later
using System.Threading;
using Microsoft.SPOT;
//using Microsoft.SPOT.IO;
using Microsoft.SPOT.Hardware;
using SecretLabs.NETMF.Hardware;
using SecretLabs.NETMF.Hardware.Netduino;
using System.IO.Ports;
using System.IO;


//Home Automation Program
//to control my room through the internet
//coded by Victor Ansart
//webserver code modeled off "Getting Started with Netduino" by Chris Walker from Make, published by O'Reilly Media
//multithreading, timing interrupts, and file IO were based off code by Philippe Chrétien from http://basbrun.com/2011/02/21/netduino-plus-part-2/
//obtaning time from npt time server class by Michael Schwarz (http://www.schwarz-interactive.de)

namespace NetduinoApplication1
{
    public class Program
    {
        //EDITABLE VALUES
        private static string externalIP = "www.MidnightMarauder.ddns.us";      //external IP for links- NEEDS TO BE SET TO EXTERNAL IP
        private static int port = 81;      //port for webserver
        private static int tempCheckDelay = 10;    //set delay between temp checks in seconds
        private static int displayDelay = 11;       //set delay between display edits in seconds
        private static bool isSummerTime = true;   //set to true if summer time, false otherwise
        private static string timeServerURL = "nist1-ny.ustiming.org";   //set time server ex.| time.nist.gov | nist.gov | nist1-ny.ustiming.org
        //more time servers @ http://tf.nist.gov/tf-cgi/servers.cgi


        //Port assignments
        private static OutputPort led = new OutputPort(Pins.ONBOARD_LED, false);
        private static OutputPort mainLight = new OutputPort(Pins.GPIO_PIN_D5, false);
        private static OutputPort deskLight = new OutputPort(Pins.GPIO_PIN_D6, false);
        private static OutputPort funLight = new OutputPort(Pins.GPIO_PIN_D7, false);
        private static AnalogInput tempSensor = new AnalogInput(AnalogChannels.ANALOG_PIN_A0);
        private static PWM red = new PWM(PWMChannels.PWM_PIN_D11, 1000, 0, true);
        private static PWM green = new PWM(PWMChannels.PWM_PIN_D10, 1000, 0, true);
        private static PWM blue = new PWM(PWMChannels.PWM_PIN_D9, 1000, 0, true);
        private static InputPort buttonA = new InputPort(Pins.GPIO_PIN_D0, false, Port.ResistorMode.PullDown);
        private static InputPort buttonB = new InputPort(Pins.GPIO_PIN_D1, false, Port.ResistorMode.PullDown);
        private static InputPort buttonC = new InputPort(Pins.GPIO_PIN_D2, false, Port.ResistorMode.PullDown);
        private static InputPort buttonD = new InputPort(Pins.GPIO_PIN_D3, false, Port.ResistorMode.PullDown);
        private static SerialPort display = new SerialPort(SerialPorts.COM4, 9600, Parity.None, 8, StopBits.None);

        //threads
        private static Thread buttons = null;

        //variable declaration
        public static double tempF = 0;
        private static int tempFInt = 0;
        private static int tempFDec = 0;
        public static double desiredTempF = 70;
        private static int option = 1;
        private static double lastDisplayChange = 0;


        //Link assignment
        private static string offAllLink = "http://" + externalIP + ":81/home*/ALL0";
        private static string onAllLink = "http://" + externalIP + ":81/home*/ALL1";
        private static string offMainLightLink = "http://" + externalIP + ":81/home*/MainLight0";
        private static string onMainLightLink = "http://" + externalIP + ":81/home*/MainLight1";
        private static string offDeskLightLink = "http://" + externalIP + ":81/home*/DeskLight0";
        private static string onDeskLightLink = "http://" + externalIP + ":81/home*/DeskLight1";
        private static string offFunLightLink = "http://" + externalIP + ":81/home*/FunLight0";
        private static string onFunLightLink = "http://" + externalIP + ":81/home*/FunLight1";


        //main function
        public static void Main()
        {
            //SETUP START
            led.Write(!led.Read());
            sendData("Loading", "internet");    //display "Loading internet" on LCD

            //WEBSERVER SETUP
            //wait for netduino to get network address
            Thread.Sleep(3000);
            //display IP address
            Microsoft.SPOT.Net.NetworkInformation.NetworkInterface
                networkInterface = Microsoft.SPOT.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()[0];

            Debug.Print("IP is: " + networkInterface.IPAddress.ToString());      //print IP to debug

            //create a socket to listen to for incoming connections
            Socket listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint listenerEndPoint = new IPEndPoint(IPAddress.Any, port);
            //bind to listening socket
            listenerSocket.Bind(listenerEndPoint);
            //and start listening for incoming connections
            listenerSocket.Listen(1);


            //INTERRUPT SETUP
            //tempCheck Timer Interrupt
            Timer tempTimer = null;     //timer to trigger temp checking functions
            TimerCallback tempCheckDelegate = new TimerCallback(tempCheck);
            tempTimer = new Timer(tempCheckDelegate, null, 1000 * tempCheckDelay, 1000 * tempCheckDelay); //set delay time in milliseconds (make both numbers the same)
            //display Timer Interrupt
            Timer displayTimer = null;
            TimerCallback displayDelegate = new TimerCallback(displayMessage);
            displayTimer = new Timer(displayDelegate, null, 1000 * displayDelay, 1000 * displayDelay);

            //PWM start
            red.Start();
            green.Start();
            blue.Start();

            //THREAD SETUP
            buttons = new Thread(buttonChecker);
            buttons.Start();

            //set datetime
            setDateTime();


            //check temp
            tempChecker();

            //display data
            displayMessages(1);

            led.Write(!led.Read());
            Debug.Print("Server initialized");
            //-----SETUP DONE---------------------------------------------------------


            //MAIN NEVERENDING LOOP
            while (true)
            {
                sendOnline(listenerSocket);     //send data online
            }     //end while true loop

        }//end Main



        //------------------------------------------------------        

        //ISR
        //timer temperature checking interrupt
        private static void tempCheck(Object stateInfo)
        {
            tempChecker();
        }

        //display editing interrupt
        private static void displayMessage(Object stateInfo)
        {
            if ((DateTime.Now.Ticks - lastDisplayChange) > 50000000)   //last disply happened less than 5 sec ago ( 10,000 ticks to 1 millisecond)
            {
                displayMessages(0);
            }
        }

        //ISR DONE

        //---------------------------------------------------------

        //FUNCTIONS
        //buttonChecker()
        //main function of button thread
        //checks to see what buttons have been pressed
        private static void buttonChecker()
        {
            //opton 1 for display is temperature
            //opton 2 for display is time and lights
            while (true)
            {
                if (buttonA.Read() == true)
                {
                    displayMessages(0);     //increment the display to show new data   
                    Thread.Sleep(500);
                }
                if (buttonB.Read() == true && option == 1)      //increase set temp
                {
                    desiredTempF++;
                    editRGB();
                    displayMessages(1);
                    Thread.Sleep(300);
                }
                if (buttonD.Read() == true && option == 1)  //decrease set temp
                {
                    desiredTempF--;
                    editRGB();
                    displayMessages(1);
                    Thread.Sleep(300);
                }
                if (buttonB.Read() == true && option == 2)     //toggle main light
                {
                    mainLight.Write(!mainLight.Read());
                    displayMessages(2);
                    Thread.Sleep(300);
                }
                if (buttonD.Read() == true && option == 2)     //toggle desk light
                {
                    deskLight.Write(!deskLight.Read());
                    displayMessages(2);
                    Thread.Sleep(300);
                }
                if (buttonC.Read() == true && option == 2)     //toggle fun light
                {
                    funLight.Write(!funLight.Read());
                    displayMessages(2);
                    Thread.Sleep(300);
                }
            }
        }

        //tempChecker()
        //edits the temp and edits RGB
        static private void tempChecker()
        {
            led.Write(!led.Read());
            double tempRead = tempSensor.Read();
            Thread.Sleep(5);
            tempRead = tempRead + tempSensor.Read();
            Thread.Sleep(5);
            tempRead = tempRead + tempSensor.Read();
            Thread.Sleep(5);
            tempRead = tempRead + tempSensor.Read();
            Thread.Sleep(5);
            tempRead = tempRead + tempSensor.Read();
            tempRead = tempRead / 5;    //average the five readings
            double tempVolt = tempRead * 3.32;   //convert reading into voltage

            double tempC = ((tempVolt * 1000) - 500) / 10;     //formula to calculate temp in Celsius
            double tempTemporary = tempC * 1.8;     //formula start to convert to temp in Farenheight
            tempF = tempTemporary + 32;             //end of formula to convert temp to Farenheight

            tempFInt = (int)tempF;
            tempFDec = (int)((tempF - tempFInt) * 10);

            //  Debug.Print("tempVolt is : " + tempVolt + "| tempC is: " + tempC + "| tempF is: " + tempF);

            //editing RGB LED
            editRGB();

            led.Write(!led.Read());
        }


        //editRGB()
        //edits the RGB color
        private static void editRGB()
        {
            if (tempF > desiredTempF)     //too hot
            {
                double redIndex = (tempF - desiredTempF) * 0.1;
                if (redIndex > 1)
                {
                    redIndex = 1;
                }
                //  Debug.Print("red brightness index = " + redIndex);
                double greenIndex = 0;
                if (redIndex < .35)
                {
                    greenIndex = .35 - redIndex;
                }
                //  Debug.Print("green brightness index = " + greenIndex);

                blue.DutyCycle = 0;
                green.DutyCycle = greenIndex;
                red.DutyCycle = redIndex;
            }
            else if (tempF < desiredTempF)   //too cold
            {
                double blueIndex = (desiredTempF - tempF) * 0.1;
                if (blueIndex > 1)
                {
                    blueIndex = 1;
                }
                //  Debug.Print("blue brightness index = " + blueIndex);
                double greenIndex = 0;
                if (blueIndex < .35)
                {
                    greenIndex = .35 - blueIndex;
                }
                //  Debug.Print("green brightness index = " + greenIndex);

                red.DutyCycle = 0;
                green.DutyCycle = greenIndex;
                blue.DutyCycle = blueIndex;
            }
            else if (tempFInt == desiredTempF)   //good temp
            {
                //   Debug.Print("GOOD TEMP!");
                red.DutyCycle = 0;
                blue.DutyCycle = 0;
                green.DutyCycle = .30;

            }
        }


        //displayMessages(option)
        //display messages on LCD
        private static void displayMessages(int select)
        {
            int maxOptions = 2;

            if (select == 0)
            {
                option++;
            }
            else
            {
                option = select;
            }
            if (option > maxOptions)
            {
                option = 1;
            }


            //OPTION 1-TEMPERATURE------------
            if (option == 1)
            {
                string message1 = "Room Temp: " + tempFInt + "." + tempFDec;
                string message2 = "Goal Temp: " + desiredTempF + ".0";

                sendData(message1, message2);
            }
            //OPTION 2-TIME & LIGHTS--------------
            else if (option == 2)
            {
                DateTime dateAndTime = DateTime.Now;
                string message1 = "    ";   //buffer to center text on LCD
                string message2 = "    ";   //buffer to center text on LCD

                //message1
                //hours
                if (dateAndTime.Hour <= 9)    //up to 9, add a 0
                    message1 += "0" + dateAndTime.Hour + ":";
                else if (dateAndTime.Hour <= 12)   // between 10 and 12, do nothing
                    message1 += dateAndTime.Hour + ":";
                else if (dateAndTime.Hour <= 21) //up to 21 hours (9 PM), convert and add a 0
                    message1 += "0" + (dateAndTime.Hour - 12) + ":";
                else //between 22(10PM) and 24 (12PM), convert
                    message1 += (dateAndTime.Hour - 12 + ":");
                //minutes
                if (dateAndTime.Minute < 10)
                    message1 += "0" + dateAndTime.Minute;
                else
                    message1 += dateAndTime.Minute;
                //AM or PM
                if (dateAndTime.Hour < 12)    //morning, AM
                    message1 += " AM";
                else                     //afternoon, PM
                    message1 += " PM";

                //message2
                //months
                if (dateAndTime.Month < 10)
                    message2 += "0" + dateAndTime.Month + "/";
                else
                    message2 += dateAndTime.Month + "/";
                //days and year
                if (dateAndTime.Day < 10)
                    message2 += "0" + dateAndTime.Day + "/" + (dateAndTime.Year % 2000);
                else
                    message2 += dateAndTime.Day + "/" + (dateAndTime.Year % 2000);

                sendData(message1, message2);
            }
            lastDisplayChange = DateTime.Now.Ticks; //udpate last time display was changed
        }

        //sendData(line1, line2);
        //send the messge to LCD screen, one line at a time
        private static void sendData(string line1, string line2)
        {
            display.Open();

            //clear display
            byte[] command = new byte[1];
            command[0] = 254;   //special command byte
            display.Write(command, 0, 1);
            command[0] = 1;     //clear screen command byte
            display.Write(command, 0, 1);

            //prep and send message 1
            byte[] buffer1 = System.Text.Encoding.UTF8.GetBytes(line1);
            display.Write(buffer1, 0, buffer1.Length);

            //move to start of 2nd line
            command[0] = 254;   //special command byte
            display.Write(command, 0, 1);
            command[0] = 128;    //next line command byte
            display.Write(command, 0, 1);
            command[0] = 16;    //line location
            display.Write(command, 0, 1);

            //prep and send message 2
            byte[] buffer2 = System.Text.Encoding.UTF8.GetBytes(line2);
            display.Write(buffer2, 0, buffer2.Length);

            display.Close();
        }



        //sendOnline(socket)
        //receives the data, analyzes it, and sends response online
        private static void sendOnline(Socket listenerSocket)
        {
            Socket clientSocket = null; //setup socket
            bool isHack = false;    //bool to see if hacking attempt
            try { clientSocket = listenerSocket.Accept(); }      //try connecting client
            catch
            {
                clientSocket = null;
                isHack = true;
                Debug.Print("Hacking attempt - below TCP level");
            }
            bool dataReady;
            if (!isHack)    //only execute if not hack
            {
                dataReady = clientSocket.Poll(-1, SelectMode.SelectRead);   //wait for data
            }
            else
            {
                dataReady = false;
            }
            //if dataReady is true AND there are bytes available to read, then have good connection
            if (!isHack && dataReady && clientSocket.Available > 0)
            {
                byte[] buffer = new byte[clientSocket.Available];

                try { int bytsRead = clientSocket.Receive(buffer); }
                catch
                {
                    isHack = true;
                    Debug.Print("Hacking attempt - receiving socket data");
                }
                //check for 
                if (buffer.Length > 1 && buffer[0] == 71 && buffer[1] == 69 && buffer[2] == 84 && buffer[5] == 104 && buffer[9] == 42)
                {
                    // Debug.Print("html buffer: " + buffer);
                    string request = "";        //initialization of string

                    try { request = new string(System.Text.Encoding.UTF8.GetChars(buffer)); }
                    catch
                    {
                        isHack = true;
                        Debug.Print("Hacking attempt - creating string from buffer");
                    }
                    // Debug.Print("html request is: " + request);


                    if (request.IndexOf("home") >= 0)     //check if URL is correct (to avoid displaying webpage to anyone accessing IP)
                    {
                        if ((request.IndexOf("ALL0") >= 0) || (request.IndexOf("OFF") >= 0))            //ALL OFF
                        {
                            led.Write(false);
                            mainLight.Write(false);
                            deskLight.Write(false);
                            funLight.Write(false);
                        }
                        else if ((request.IndexOf("ALL1") >= 0) || (request.IndexOf("ON") >= 0))        //ALL ON
                        {
                            led.Write(true);
                            mainLight.Write(true);
                            deskLight.Write(true);
                            funLight.Write(true);
                        }

                        else if (request.IndexOf("MainLight1") >= 0)        //mainLight ON
                        {
                            mainLight.Write(true);
                        }
                        else if (request.IndexOf("MainLight0") >= 0)        //mainLight OFF
                        {
                            mainLight.Write(false);
                        }

                        else if (request.IndexOf("DeskLight1") >= 0)      //LED ON
                        {
                            deskLight.Write(true);
                        }
                        else if (request.IndexOf("DeskLight0") >= 0)      //LED OFF
                        {
                            deskLight.Write(false);
                        }

                        else if (request.IndexOf("FunLight1") >= 0)        //funLight ON
                        {
                            funLight.Write(true);
                        }
                        else if (request.IndexOf("FunLight0") >= 0)        //funLight OFF
                        {
                            funLight.Write(false);
                        }

                        //strings to put in webpage
                        string statusMainLight = "Main Light is " + (mainLight.Read() ? "ON" : "OFF") + ".";
                        string statusDeskLight = "Desk Light is " + (deskLight.Read() ? "ON" : "OFF") + ".";
                        string statusFunLight = "Fun Light is " + (funLight.Read() ? "ON" : "OFF") + ".";


                        //webpage to let user know of status of devices

                        string response =

                        "HTTP/1.1 200 OK\r\n" +
                        "Content-Type : text/html; charset=utf-8\r\n\r\n" +
                        "<html><head><title>Vito's Room</title></head>" +
                        "<body> " + "<h1><b>Vito's Home Automation</b></h1>" +
                        "<p><b>The connected devices and their status are:</b></p>" +

                        "<p>" + statusMainLight + "<br>" +
                        "Turn the Main Light " + "<a href=" + onMainLightLink + ">ON</a> or <a href=" + offMainLightLink + ">OFF</a></p>" +

                        "<p>" + statusDeskLight + "<br>" +
                        "Turn the Desk Light " + "<a href=" + onDeskLightLink + ">ON</a> or <a href=" + offDeskLightLink + ">OFF</a></p>" +

                        "<p>" + statusFunLight + "<br>" +
                        "Turn the Fun Lights " + "<a href=" + onFunLightLink + ">ON</a> or <a href=" + offFunLightLink + ">OFF</a></p>" +

                        "<p>" + "Turn ALL devices " + "<a href=" + onAllLink + ">ON</a> or <a href=" + offAllLink + ">OFF</a></p>" +

                        "<p>Room temperature is: " + tempF + " degrees Farenheight</p>" +
                            // "<meta http-equiv=Refresh content='10'>" +  //line to auto refresh 
                        "</body><html>";


                        clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(response));
                    }
                }

                //close client socket - IMPORTANT!!
                clientSocket.Close();
            }
        }       //end sendOnline

        //SDconnected()
        //checks to see if an SD card is connected
       /* private bool SDconnected()
        {
           // VolumeInfo[] volumes = VolumeInfo.GetVolumes;
            StreamWriter streamWriter = new StreamWriter(file);
            
            return false;
        }
        */

        //setDatetime()
        //sets the netduino's time to time obtained from network
        private static void setDateTime()
        {
            sendData("Fetching", "date & time");
            double adjustUTC = 0;
            DateTime currentTime = GetNetworkTime(timeServerURL);
            if (isSummerTime)
            {
                adjustUTC = -4;
            }
            else
            {
                adjustUTC = -5;
            }
            currentTime = currentTime.AddHours(adjustUTC);
            Debug.Print("Local time is: " + currentTime.ToString());
            Utility.SetLocalTime(currentTime);
        }

        /// <summary>
        /// Gets the current DateTime from <paramref name="ntpServer"/>.
        /// </summary>
        /// <param name="ntpServer">The hostname of the NTP server.</param>
        /// <returns>A DateTime containing the current time.</returns>
        private static DateTime GetNetworkTime(string ntpServer)
        {
            IPAddress[] address = Dns.GetHostEntry(ntpServer).AddressList;

            if (address == null || address.Length == 0)
                throw new ArgumentException("Could not resolve ip address from '" + ntpServer + "'.", "ntpServer");

            IPEndPoint ep = new IPEndPoint(address[0], 123);

            Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            s.Connect(ep);

            byte[] ntpData = new byte[48]; // RFC 2030 
            ntpData[0] = 0x1B;
            for (int i = 1; i < 48; i++)
                ntpData[i] = 0;

            s.Send(ntpData);
            s.Receive(ntpData);

            byte offsetTransmitTime = 40;
            ulong intpart = 0;
            ulong fractpart = 0;

            for (int i = 0; i <= 3; i++)
                intpart = 256 * intpart + ntpData[offsetTransmitTime + i];

            for (int i = 4; i <= 7; i++)
                fractpart = 256 * fractpart + ntpData[offsetTransmitTime + i];

            ulong milliseconds = (intpart * 1000 + (fractpart * 1000) / 0x100000000L);
            s.Close();

            TimeSpan timeSpan = TimeSpan.FromTicks((long)milliseconds * TimeSpan.TicksPerMillisecond);

            DateTime dateTime = new DateTime(1900, 1, 1);
            dateTime += timeSpan;

            TimeSpan offsetAmount = TimeZone.CurrentTimeZone.GetUtcOffset(dateTime);
            DateTime networkDateTime = (dateTime + offsetAmount);

            return networkDateTime;
        }



    }   //end program
}   //end namespace


