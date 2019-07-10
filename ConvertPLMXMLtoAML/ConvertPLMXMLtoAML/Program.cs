using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Aras;
using Aras.IOM;
using System.Configuration;
using System.Reflection;
using System.Web;
using Outlook = Microsoft.Office.Interop.Outlook;
using System.Threading;
using log4net;
using log4net.Repository.Hierarchy;
using log4net.Appender;

namespace ConvertPLMXMLtoAML
{
    class Program
    {
        //Maintaining 3 log files- User log, Technical log , Error log.
        private static readonly ILog user_log = LogManager.GetLogger("RollingLogFileAppender1");
        private static readonly ILog error_log = LogManager.GetLogger("RollingLogFileAppender2");
        private static readonly ILog technical_log = LogManager.GetLogger("RollingLogFileAppender3");

        static string initiatorName = "";
        static string reRunFileName = "";
        static string pLMXMLFolderPath = "";
        static string pLMXMLFileName = "";
        static string textFile = "";
        static string changeType = "";
        static string changeNumber = "";
        static string Transaction_id = "";
        static string Transaction_type = "";
        static string TransactionLogID;
        static string TransactionStatusID;
        static string previousfileName = "";


        static XmlDocument mappingXMLDoc = new XmlDocument();
        static List<partTypes> PartTypeList = new List<partTypes>();
        static Hashtable xmlArasIDMapping = new Hashtable();

        static Hashtable itemPropertyMapping = new Hashtable();
        static Hashtable itemRevPropertyMapping = new Hashtable();

        static XmlNamespaceManager oManager = new XmlNamespaceManager(new NameTable());

        static HttpServerConnection conn = null;
        static Innovator inn = null;
        static bool isSuccess = true;
        static bool isProcessSuccess = true;
       // public static string Title { get; set; }

        static void Main(string[] args)
        {
            //Maintain log files 
            ImplementLoggingFuntion();

            //Code to read parameters through command line arguements
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index].Trim().ToLower().Equals("-plmxmlfolderpath"))
                {
                    pLMXMLFolderPath = args[++index];
                    user_log.Info("Read Folder Path Successfully:" + pLMXMLFolderPath);

                }
                else if (args[index].Trim().ToLower().Equals("-textfile"))
                {
                    textFile = args[++index];
                    user_log.Info("Read Text file Path Successfully:" + textFile);
                }
                else if (args[index].Trim().ToLower().Equals("-changetype"))
                {
                    changeType = args[++index];
                }
                else if (args[index].Trim().ToLower().Equals("-changenumber"))
                {
                    changeNumber = args[++index];
                }
                else if (args[index].Trim().ToLower().Equals("-user"))
                {
                    initiatorName = args[++index];
                }
                else if (args[index].Trim().ToLower().Equals("-rerunFilePath"))
                {
                    reRunFileName = args[++index];
                }
            }

            string consoleTitle = Path.GetFileName(pLMXMLFolderPath);
            Console.Title = consoleTitle;

            logintoAras();

            if (conn == null)
            {
                Console.WriteLine("Error while connecting to Aras.");
                error_log.Error("Connection Failed To ARAS");
                return;
            }

            oManager.AddNamespace("ns", "http://www.plmxml.org/Schemas/PLMXMLSchema");

            mappingXMLDoc.Load(@".\TCtoArasMapping.xml");

            //Get the Part Types in Mapping Sheet 
            ProcessMappingXML();

            //Generating unique transaction id on every execution
            TransactionID();
           
            String[] arguments = Environment.GetCommandLineArgs();
            arguments = arguments.Where(w => w != arguments[0]).ToArray();

            //Transcation Status Update - Initiation of Diaspora
            Item it = inn.newItem("CS_Transaction_Status", "add");
            it.setProperty("source_id", TransactionLogID);
            it.setProperty("cs_transaction_path", "Initiation of Diaspora");
            it.setProperty("cs_transaction_action", "Initiation");
            it.setProperty("cs_transaction_time", DateTime.Now.ToString());
            it.setProperty("cs_transaction_status", "Started");
            it.setProperty("cs_transaction_user", ConfigurationManager.AppSettings["userid"]);
            Item itm = it.apply();
            if (pLMXMLFolderPath != null && textFile != null)
            {
                itm.setAction("edit");
                itm.setProperty("cs_parameters", String.Join(" ", arguments));
                itm.setProperty("cs_details", String.Join(" ", arguments) + '\t' + "has been updated successfully.");
                itm.setProperty("cs_transaction_status", "Success");
                Item item = itm.apply();
            }
            else
            {
                itm.setAction("edit");
                itm.setProperty("cs_details", "unable to read the PLMXML folder path or the text file");
                itm.setProperty("cs_transaction_status", "Failed");
                Item item = itm.apply();
            }

            //Reading multiple PLMXMLFiles and processing one after other 
            
            try
            {
                String textFilePath = Path.Combine(pLMXMLFolderPath, textFile);

                string[] lines = File.ReadAllLines(textFilePath);

                for (int ind = 0; ind < lines.Length; ind++)
                {
                    //isProcessSuccess = true;
                    pLMXMLFileName = lines[ind];
                    xmlArasIDMapping.Clear();
                    // Object newArasID = "";
                    // revElementId = "";
                    //xmlArasIDMapping.Add(revElementId, newArasID);
                    ProcessPLMXML(pLMXMLFolderPath, pLMXMLFileName);                    

                }

                
            }
            catch (Exception ex)
            {
                isProcessSuccess = false;
                isSuccess = false;
                Console.WriteLine("File is not found at specified path.Please check if specified path is correct.");
                error_log.Error("File Not Found:" + ex.Message);

                itm.setAction("edit");
                itm.setProperty("cs_details", "unable to read the PLMXML folder path or the text file \n" + ex.Message);
                itm.setProperty("cs_transaction_status", "Failed");
                Item item = itm.apply();

                String transactionLogStatusAML =
                                                    "<AML>" +
                                                        "<Item type='CS_Transaction_Log' action='merge' id='" + TransactionLogID + "'>" +
                                                            "<cs_transaction_log_status>Failed</cs_transaction_log_status>" +
                                                        "</Item>" +
                                                    "</AML>";
                Item restAMl = inn.applyAML(transactionLogStatusAML);

                return;
            }

            //Attach Log Files to Transaction Log
            Transaction_log();

            //Promote Aras WorkFlow - on the basis of integration status failed/success. 
            promoteRespectiveWF();

            //To delete the folder if transaction is successfull.
            deletePLMXMLFolder();

            if (isSuccess)
            {
                String transactionLogStatusAML =
                                                "<AML>" +
                                                    "<Item type='CS_Transaction_Log' action='merge' id='" + TransactionLogID + "'>" +
                                                        "<cs_transaction_log_status>Success</cs_transaction_log_status>" +
                                                    "</Item>" +
                                                "</AML>";
                Item restAMl = inn.applyAML(transactionLogStatusAML);
            }
            else
            {
                String transactionLogStatusAML =
                                                "<AML>" +
                                                    "<Item type='CS_Transaction_Log' action='merge' id='" + TransactionLogID + "'>" +
                                                        "<cs_transaction_log_status>Failed</cs_transaction_log_status>" +
                                                    "</Item>" +
                                                "</AML>";
                Item restAMl = inn.applyAML(transactionLogStatusAML);
            }

            if (conn != null)
            {
                conn.Logout();
                user_log.Info("Logout Successfull!");
            }
            //Console.ReadLine();
        }

        private static void logintoAras()
        {
            string arasUrl = ConfigurationManager.AppSettings["url"];
            string Userid = ConfigurationManager.AppSettings["userid"];
            string DatabaseName = ConfigurationManager.AppSettings["database"];
            string Password = ConfigurationManager.AppSettings["password"];

            conn = login(arasUrl, DatabaseName, Userid, Password);
            if (conn != null)
            {
                inn = IomFactory.CreateInnovator(conn);
                // In case of ARAS Connection - Succesfull delete reRunFile immediately.
                //string del_file = Path.Combine(pLMXMLFolderPath, reRunFileName);
                if (File.Exists(reRunFileName))
                {
                    File.Delete(reRunFileName);
                }
            }
        }

        internal static HttpServerConnection login(string arasURL, string dataBaseName, string userid, string userPass)
        {
            String[] urlarray = arasURL.Split('/');

            String protocal = urlarray[0];
            String ServerName = urlarray[2];
            String Site = urlarray[3];

            String url = @protocal + "//" + ServerName + "/" + Site;
            //String url = ArasURL;
            String db = dataBaseName;
            String user = userid;
            String password = userPass;
            HttpServerConnection connect = IomFactory.CreateHttpServerConnection(url, db, user, password);
            Item login_result = connect.Login();

            user_log.Info("Login Succesfull!");

            if (login_result.isError())
            {
                // MessageBox.Show(login_result.getErrorString().Replace("SOAP-ENV:ServerAuthentication failed for admin", ""), "Login Failed");
                isSuccess = false;
                Console.WriteLine("Login failed to aras");
                error_log.Error("Login failed to aras" + login_result.getErrorString());
                return null;
                // throw new Exception("Login failed :-" + login_result.getErrorString());
            }

            
            return connect;

        }

        private static void TransactionID()
        {
            Transaction_id = $@"T-{Guid.NewGuid()}";
            Transaction_type = "TC TO ARAS";

            Item it = inn.newItem("CS_Transaction_Log", "add");
            it.setProperty("cs_transactio_id", Transaction_id);
            it.setProperty("cs_integration_type", Transaction_type);
            it.setProperty("cs_initiator_name", initiatorName);
            it.setProperty("cs_transaction_log_status", "Started");

            if (changeNumber == "" && changeType == "")
            {
                String ContextItem = getContextItem();
                it.setProperty("cs_transaction_context", "On Demand");
                it.setProperty("cs_transaction_context_item", ContextItem);
            }
            else
            {
                it.setProperty("cs_transaction_context", changeType);
                it.setProperty("cs_transaction_context_item", changeNumber);
            }

            it.apply();

            TransactionLogID = it.getID();
        }

        private static void Transaction_log()
        {

            //string[] file_name = new string[2];
            string logFilePath = "";
            string logFileName = "";
            for (int index = 0; index < ((Hierarchy)LogManager.GetRepository()).GetAppenders().Length; index++)
            {
                //get log file path from the config file
                logFilePath = (LogManager.GetCurrentLoggers()[index].Logger.Repository.GetAppenders()[index] as FileAppender).File;
                logFileName = Path.GetFileName(logFilePath);

                Item fileObj = inn.newItem("File", "add");
                fileObj.setProperty("filename", logFileName);
                fileObj.attachPhysicalFile(logFilePath);
                fileObj = fileObj.apply();
                string NativeFileId = fileObj.getID();
                if(index == 0)
                { 
                Item user_logfile = inn.newItem("CS_Transaction_LogFiles", "add");
                user_logfile.setProperty("source_id", TransactionLogID);
                user_logfile.setProperty("cs_run","Initial" );
                user_logfile.setProperty("cs_user_log", NativeFileId);
                Item item_user_logfile = user_logfile.apply();
                }
                if(index == 1)
                {
                        Item technical_logfile = inn.newItem("CS_Transaction_LogFiles", "edit");
                        technical_logfile.setAttribute("where", "[CS_Transaction_LogFiles].source_id = '" + TransactionLogID + "' ");
                        technical_logfile.setProperty("cs_technical_log", NativeFileId);
                        Item item_technical_logfile = technical_logfile.apply();

                 }
                if (index == 2)
                {
                        Item error_logfile = inn.newItem("CS_Transaction_LogFiles", "edit");
                        error_logfile.setAttribute("where", "[CS_Transaction_LogFiles].source_id = '" + TransactionLogID + "' ");
                        error_logfile.setProperty("cs_error_log", NativeFileId);
                        Item item_error_logfile = error_logfile.apply();
                }
                   
            }

        }

        private static void ProcessPLMXML(string pLMXMLFolderPath, string pLMXMLFileName)
        {
            isProcessSuccess = true;

            Item ts_Item = inn.newItem("CS_Transaction_Status", "add");
            ts_Item.setProperty("source_id", TransactionLogID);
            ts_Item.setProperty("cs_transaction_path", "Processing PLMXML");
            ts_Item.setProperty("cs_transaction_action", "Processing");
            ts_Item.setProperty("cs_transaction_time", DateTime.Now.ToString());
            ts_Item.setProperty("cs_transaction_status", "Started");
            ts_Item.setProperty("cs_transaction_user", ConfigurationManager.AppSettings["userid"]);
            ts_Item.setProperty("cs_details", pLMXMLFileName + '\t' + "is processing now.");

            Item ts_ItemResult = ts_Item.apply();
            TransactionStatusID = ts_ItemResult.getID();


            StringBuilder finalAMLScript = new StringBuilder();
            StringBuilder addItemsScript = new StringBuilder();
            StringBuilder addBOMScript = new StringBuilder();

            //Binding AML for change Item
            StringBuilder changeBOMScript = new StringBuilder();

            //String pLMXMLFilePath = Path.Combine(pLMXMLFolderPath, pLMXMLFileName);
            XmlDocument plmXMLDoc = new XmlDocument();
            plmXMLDoc.Load(pLMXMLFileName);

            //Processing ItemCreation
            foreach (partTypes partType in PartTypeList)
            {
                
                // PartTypeList[0].
                addItemsScript.Append(GetAddItemAMLforType(plmXMLDoc, partType));

            }
            finalAMLScript.Append(addItemsScript);

            //Processing BOM generation
            finalAMLScript.AppendLine();
            addBOMScript.Append(getBOMAML(plmXMLDoc));
            finalAMLScript.Append(addBOMScript);

            //Processing change Item
            finalAMLScript.AppendLine();
            changeBOMScript.Append(addPartsToChange(plmXMLDoc));
            finalAMLScript.Append(changeBOMScript);


            if(isProcessSuccess)
            {
                ts_ItemResult.setAction("edit");
                ts_ItemResult.setProperty("cs_transaction_status", "Success");
                ts_ItemResult = ts_ItemResult.apply();
            }
            else
            {
                ts_ItemResult.setAction("edit");
                ts_ItemResult.setProperty("cs_transaction_status", "Failed");
                ts_ItemResult = ts_ItemResult.apply();
            }

            //Console.Write(finalAMLScript);

        }

        private static string getBOMAML(XmlDocument plmXMLDoc)
        {
            String bomAML = "";

            String occuranceXPath = "//ns:Occurrence";

            List<string> parentArasIDList = new List<string>();

            XmlNodeList occuranceNodeList = plmXMLDoc.SelectNodes(occuranceXPath, oManager);

            foreach (XmlNode occuranceNode in occuranceNodeList)
            {
                //get the Parent ID
                String parentIDNum = occuranceNode.Attributes["instancedRef"].Value;
                String parentxmlID = parentIDNum.Remove(0, 1);
                String parentArasID = xmlArasIDMapping[parentxmlID].ToString();


                //Remove BOM 
                if (parentArasIDList.Contains(parentArasID) == false)
                {
                    string id = parentArasID;
                    Item item = inn.getItemById("Part", id);
                    item.fetchRelationships("Part BOM");

                    Item itm = inn.newItem("Part BOM", "delete");
                    itm.setAttribute("where", "[Part_BOM].source_id='" + id + "'");

                    Item tsAML_Item = inn.newItem("CS_Transaction_Status_AML", "add");
                    tsAML_Item.setProperty("source_id", TransactionStatusID);
                    tsAML_Item.setProperty("cs_result", itm.ToString());
                    tsAML_Item.setProperty("cs_aml_status", "Started");
                    Item tsAML_ItemResult = tsAML_Item.apply();

                    itm = itm.apply();
                    if(itm.isError())
                    {
                        isSuccess = false;
                        isProcessSuccess = false;
                        tsAML_ItemResult.setAction("edit");
                        tsAML_ItemResult.setProperty("cs_aml_status", "Failed");
                        tsAML_ItemResult.setProperty("cs_aml_details", itm.getErrorString());
                        tsAML_ItemResult = tsAML_ItemResult.apply();

                    }
                    else
                    {
                        tsAML_ItemResult.setAction("edit");
                        tsAML_ItemResult.setProperty("cs_aml_status", "Success");
                        tsAML_ItemResult = tsAML_ItemResult.apply();
                    }

                    parentArasIDList.Add(parentArasID);
                }

                //get Child IDs

                XmlAttribute occrefAttr = occuranceNode.Attributes["occurrenceRefs"];
                if (occrefAttr == null)
                {
                    continue;
                }

                String childIDs = occrefAttr.Value;
                string[] childIDArray = childIDs.Split(' ');

                foreach (String childOccxmlID in childIDArray)
                {
                    XmlNode childOccNode = plmXMLDoc.SelectSingleNode("//ns:Occurrence[@id='" + childOccxmlID + "']", oManager);
                    string childxmlIDNum = childOccNode.Attributes["instancedRef"].Value;
                    string childxmlID = childxmlIDNum.Remove(0, 1);
                    String childItemArasId = xmlArasIDMapping[childxmlID].ToString();
                    string bomPropAML = getBOMPropertiesAML(parentxmlID, childxmlID);


                    bomAML = "<AML><Item type='Part BOM' action='add'>" +
                             "<source_id>" + parentArasID + "</source_id>" +
                             "<related_id>" + childItemArasId + "</related_id>" +
                             bomPropAML +
                             "</Item></AML>";

                    

                    Item it = inn.newItem("CS_Transaction_Status_AML", "add");
                    it.setProperty("source_id", TransactionStatusID);
                    it.setProperty("cs_result", bomAML);
                    it.setProperty("cs_aml_status", "Started");
                    Item tsAML_ItemResult1 = it.apply();

                    Item BOMResult = inn.applyAML(bomAML);

                    //if (BOMResult.isError())
                    //{
                    //    isSuccess = false;
                    //    isProcessSuccess = false;
                    //    tsAML_ItemResult1.setAction("edit");
                    //    tsAML_ItemResult1.setProperty("cs_aml_status", "Failed");
                    //    tsAML_ItemResult1.setProperty("cs_aml_details", BOMResult.getErrorString());
                    //    tsAML_ItemResult1 = tsAML_ItemResult1.apply();

                    //}
                    //else
                    //{
                    //    tsAML_ItemResult1.setAction("edit");
                    //    tsAML_ItemResult1.setProperty("cs_aml_status", "Success");
                    //    tsAML_ItemResult1 = tsAML_ItemResult1.apply();
                    //}

                    technical_log.Debug("BOM AML :" + bomAML);

                    Item transaction_status = inn.newItem("CS_Transaction_Status", "edit");
                    transaction_status.setAttribute("where", "[CS_Transaction_Status].id = '" + TransactionStatusID + "' ");

                    if (BOMResult.isError())
                    {
                        isSuccess = false;
                        Console.WriteLine("Part BOM AML Error :" + BOMResult.getErrorString());
                        user_log.Error("Error while adding Part BOM Structure :" + BOMResult.getErrorString());

                        isSuccess = false;
                        isProcessSuccess = false;
                        tsAML_ItemResult1.setAction("edit");
                        tsAML_ItemResult1.setProperty("cs_aml_status", "Failed");
                        tsAML_ItemResult1.setProperty("cs_aml_details", BOMResult.getErrorString());
                        tsAML_ItemResult1 = tsAML_ItemResult1.apply();

                        //transaction_status.setProperty("cs_transaction_status", "Failed");
                        //transaction_status.apply();
                        //isProcessSuccess = false;
                    }
                    else
                    {
                        tsAML_ItemResult1.setAction("edit");
                        tsAML_ItemResult1.setProperty("cs_aml_status", "Success");
                        tsAML_ItemResult1 = tsAML_ItemResult1.apply();
                    }

                    //if (isSuccess)
                    //{
                    //    transaction_status.setProperty("cs_transaction_status", "Success");
                    //    transaction_status.apply();
                    //}
                }

            }
            return bomAML;
        }

        private static string getContextItem()
        {
            String contextItem = "";

            if (changeNumber == "" && changeType == "")
            {
                String textFilePath = Path.Combine(pLMXMLFolderPath, textFile);

                string[] lines = File.ReadAllLines(textFilePath);

                IList<String> contextItemsList = new List<String>();

                for (int ind = 0; ind < lines.Length; ind++)
                {
                    //isProcessSuccess = true;
                    pLMXMLFileName = lines[ind];
                    xmlArasIDMapping.Clear();

                    String tempcontextItem = Path.GetFileName(pLMXMLFileName).Replace(".xml", "");
                    contextItemsList.Add(tempcontextItem);

                }
                contextItem = string.Join(",", contextItemsList.ToArray());

            }
            else
            {
                contextItem = changeNumber;
            }

                return contextItem;
        }

        private static string addPartsToChange(XmlDocument plmXMLDoc)
        {
            if (changeNumber == "" && changeType == "")
            {
                string contextAML = "";
                              
                //string contextItem = Path.GetFileName(pLMXMLFileName).Replace(".xml", "");

                //if (!string.IsNullOrEmpty(previousfileName))
                //{
                //    contextItem= previousfileName + ","+ contextItem;
                //    previousfileName = contextItem;
                //}
                //else
                //{
                //    previousfileName = contextItem;
                //}
                
                //contextAML = "<AML>" +
                //             "<Item type='CS_Transaction_Log' action ='edit' where=\"[CS_Transaction_Log].cs_transactio_id='" + Transaction_id + "'\">" +
                //             "<cs_transaction_context>onDemand</cs_transaction_context>" +
                //             "<cs_transaction_context_item>" + contextItem + "</cs_transaction_context_item>" +
                //             "</Item>" +
                //             "</AML>";
                //Item contextaml_result = inn.applyAML(contextAML);

                return contextAML;

            }
            else
            {
                string changeAML = "";
                
                //Item item = inn.newItem("CS_Transaction_Log", "edit");
                //item.setAttribute("where", "[CS_Transaction_Log].id = '" + TransactionLogID + "' ");
                //item.setProperty("cs_transaction_context", changeType);
                //item.setProperty("cs_transaction_context_item", changeNumber);
                //Item item_change_number = item.apply();
                try
                {
                    String occuranceXPath = "//ns:Occurrence";
                    XmlNodeList occuranceNodeList = plmXMLDoc.SelectNodes(occuranceXPath, oManager);

                    foreach (XmlNode occuranceNode in occuranceNodeList)
                    {
                        //get the Parent ID
                        String parentIDNum = occuranceNode.Attributes["instancedRef"].Value;
                        String parentxmlID = parentIDNum.Remove(0, 1);
                        String parentArasID = xmlArasIDMapping[parentxmlID].ToString();

                        //get Child IDs
                        XmlAttribute occrefAttr = occuranceNode.Attributes["occurrenceRefs"];
                        //if (occrefAttr == null)
                        //{
                        //    continue;
                        //}
                        XmlAttribute parentref = occuranceNode.Attributes["parentRef"];
                        if (parentref == null)
                        {
                            string var_prt_id = parentArasID;
                            int part_exist = 0;

                            String AffectedPartRelationshipAMl =  "";

                            AffectedPartRelationshipAMl = "<AML>" +
                                               "<Item type = '"+ changeType + " Affected Item' action ='get'>" +
                                               "<related_id>" +
                                               "<Item type ='Affected Item' action ='add'>" +
                                               "<affected_id>" + var_prt_id + "</affected_id>" +
                                               "</Item>" +
                                               "</related_id>" +
                                               "<source_id>" +
                                               "<Item type = '" + changeType + "' action = 'get' where =\"[" + changeType + "].item_number ='" + changeNumber + " '\">" +
                                               "</Item>" +
                                               "</source_id>" +
                                               "</Item>" +
                                               "</AML>";

                            //Item cs_it = inn.newItem("CS_Transaction_Status_AML", "add");
                            //cs_it.setProperty("source_id", TransactionStatusID);
                            //cs_it.setProperty("cs_result", AffectedPartRelationshipAMl);
                            //cs_it.setProperty("cs_aml_status", "Started");
                            //Item ite = cs_it.apply();

                            Item it = inn.applyAML(AffectedPartRelationshipAMl);
                            //    inn.newItem(changeType, "get");
                            //it.setProperty("item_number", changeNumber);
                            //String changeItemAml = it.ToString();
                            //it = it.apply();

                            //if(it.isError())
                            //{
                            //    isSuccess = false;                                

                            //    ite.setAction("edit");
                            //    ite.setProperty("cs_aml_status", "Failed");
                            //    ite.setProperty("cs_aml_details", it.getErrorString());
                            //    ite.apply();

                            //    Item transaction_status = inn.newItem("CS_Transaction_Status", "edit");
                            //    transaction_status.setAttribute("where", "[CS_Transaction_Status].id = '" + TransactionStatusID + "' ");
                            //    transaction_status.setProperty("cs_transaction_status", "Failed");
                            //    transaction_status.apply();
                            //    //Console.WriteLine("Part BOM AML Error :" + changeResult.getErrorString());
                            //    //user_log.Error("Error while adding Part BOM Structure :" + changeResult.getErrorString());
                            //}
                            //else
                            //{
                            //    ite.setAction("edit");
                            //    ite.setProperty("cs_aml_status", "Success");
                            //    ite.setProperty("cs_aml_details", it.getErrorString());
                            //    ite.apply();
                            //}

                            Console.WriteLine("Cont of it.getItemCount--> "+ it.getItemCount());
                            
                            if (it.getItemCount() <= 0)
                            {
                                changeAML = "<AML>" +
                                               "<Item type = '"+ changeType + " Affected Item' action ='add'>" +
                                               "<related_id>" +
                                               "<Item type ='Affected Item' action ='add'>" +
                                               "<affected_id>" + var_prt_id + "</affected_id>" +
                                               "</Item>" +
                                               "</related_id>" +
                                               "<source_id>" +
                                               "<Item type = '" + changeType + "' action = 'get' where =\"[" + changeType + "].item_number ='" + changeNumber + " '\">" +
                                               "</Item>" +
                                               "</source_id>" +
                                               "</Item>" +
                                               "</AML>";                                
                                
                                Item cs_it1 = inn.newItem("CS_Transaction_Status_AML", "add");
                                cs_it1.setProperty("source_id", TransactionStatusID);
                                cs_it1.setProperty("cs_result", changeAML);
                                cs_it1.setProperty("cs_aml_status", "Started");
                                Item ite1 = cs_it1.apply();

                                Item changeResult = inn.applyAML(changeAML);

                                if (changeResult.isError())
                                {
                                    isProcessSuccess = false;
                                    isSuccess = false;

                                    ite1.setAction("edit");
                                    ite1.setProperty("cs_aml_status", "Failed");
                                    ite1.setProperty("cs_aml_details", changeResult.getErrorString());
                                    ite1.apply();

                                    //Item transaction_status = inn.newItem("CS_Transaction_Status", "edit");
                                    //transaction_status.setAttribute("where", "[CS_Transaction_Status].id = '" + TransactionStatusID + "' ");
                                    //transaction_status.setProperty("cs_transaction_status", "Failed");
                                    //transaction_status.apply();
                                    //Console.WriteLine("Part BOM AML Error :" + changeResult.getErrorString());
                                    //user_log.Error("Error while adding Part BOM Structure :" + changeResult.getErrorString());
                                }
                                else
                                {
                                    ite1.setAction("edit");
                                    ite1.setProperty("cs_aml_status", "Success");
                                    ite1.setProperty("cs_aml_details", changeResult.getErrorString());
                                    ite1.apply();
                                }
                                //if (isSuccess)
                                //{
                                //    Item transaction_status = inn.newItem("CS_Transaction_Status", "edit");
                                //    transaction_status.setAttribute("where", "[CS_Transaction_Status].id = '" + TransactionStatusID + "' ");
                                //    transaction_status.setProperty("cs_transaction_status", "Success");
                                //    transaction_status.apply();
                                //}
                            }

                            return changeAML;

                        }

                    }
                    return changeAML;
                }
                catch(Exception ex)
                {
                    isProcessSuccess = false;
                    isSuccess = false;

                    Console.WriteLine("Change Number & Change Type specified is doesn't exist in the system");
                    Item cs_it = inn.newItem("CS_Transaction_Status_AML", "add");
                    cs_it.setProperty("source_id", TransactionStatusID);
                    cs_it.setProperty("cs_result", changeAML);
                    cs_it.setProperty("cs_aml_status", "Failed");
                    Item ite = cs_it.apply();

                    //Item transaction_status = inn.newItem("CS_Transaction_Status", "edit");
                    //transaction_status.setAttribute("where", "[CS_Transaction_Status].id = '" + TransactionStatusID + "' ");
                    //transaction_status.setProperty("cs_transaction_status", "Failed");
                    //transaction_status.apply();
                }
                return changeAML;
            }
        }

        private static string getBOMPropertiesAML(string parentxmlID, string childxmlID)
        {
            String bompropaml = "";
            return bompropaml;
        }

        private static string GetAddItemAMLforType(XmlDocument plmXMLDoc, partTypes partType)
        {
            //Transcation Status Update - Processing PLMXML
            

            String AML = "";
            String tcItemTtpe = partType.tcItemType;
            String tcRevType = partType.tcRevType;
            String arasClass = partType.arasClass;
            String itemIdAtt = partType.itemIdAtt;
            String tagTypeinXML = partType.tagTypeinXML;
            String arasItemType = partType.arasItemType;

            String itemXPath = "//ns:" + tagTypeinXML + "[@subType='" + tcItemTtpe + "']";

            XmlNodeList ItemtypeNodeList = plmXMLDoc.SelectNodes(itemXPath, oManager);

            foreach (XmlNode ItemtypeNode in ItemtypeNodeList)
            {
                //reaading Item Element
                String itemId = ItemtypeNode.Attributes[itemIdAtt].Value;
                String itemName = ItemtypeNode.Attributes["name"].Value;
                String itemElementID = ItemtypeNode.Attributes["id"].Value;

                string itemPropAML = addItemProperties(ItemtypeNode, tcItemTtpe);

                //read Revision Elements
                String revXPath = "//ns:" + tagTypeinXML + "Revision[@subType='" + tcRevType + "' and @masterRef='#" + itemElementID + "']";
                XmlNodeList ItemRevtypeNodeList = plmXMLDoc.SelectNodes(revXPath, oManager);

                foreach (XmlNode ItemRevtypeNode in ItemRevtypeNodeList)
                {
                    String major_rev = ItemRevtypeNode.Attributes["revision"].Value;
                    String revElementId = ItemRevtypeNode.Attributes["id"].Value;

                    String status_ref_id = "";
                    if(ItemRevtypeNode.Attributes["releaseStatusRefs"] != null)
                    {
                        status_ref_id = ItemRevtypeNode.Attributes["releaseStatusRefs"].Value;
                    }

                    String itemAction = "";
                    //String newArasID = ++partid+"0000";//getArasID(arasItemType,itemId,major_rev);
                    String newArasID = getArasID(arasItemType, itemId, major_rev, out itemAction);

                    if (String.IsNullOrEmpty(itemAction) && String.IsNullOrEmpty(newArasID))
                    {
                        continue;
                    }

                    if (itemAction == "version")
                    {
                        newArasID = getNewRevisionID(arasItemType, newArasID);

                        if (String.IsNullOrEmpty(newArasID))
                        {
                            continue;
                        }

                    }

                    String releaseDate = "";
                    string releaseStatus = getReleaseStatus(ItemRevtypeNode, tcRevType, status_ref_id, plmXMLDoc, out releaseDate);

                    string itemRevPropAML = addItemRevProperties(ItemRevtypeNode, tcRevType, tagTypeinXML + "Revision", plmXMLDoc, revElementId);
                    string addFilesAML = addCADAndOtherFiles(ItemRevtypeNode, tcRevType, tagTypeinXML + "Revision", plmXMLDoc, revElementId, itemId, major_rev, releaseStatus, releaseDate, newArasID);

                    

                    AML = "<AML><Item type='" + arasItemType + "' action='merge' id='" + newArasID + "'>" +
                        "<item_number>" + itemId + "</item_number>" +
                        "<major_rev>" + major_rev + "</major_rev>" +
                        "<name>" + itemName + "</name>" +
                        itemPropAML +
                        itemRevPropAML +
                        //releaseStatusAML+
                        addFilesAML +
                        "</Item></AML>";
                    
                    Item it = inn.newItem("CS_Transaction_Status_AML", "add");
                    it.setProperty("source_id", TransactionStatusID);
                    it.setProperty("cs_result", AML);
                    it.setProperty("cs_aml_status", "Started");
                    Item ite = it.apply();

                    Item Result = inn.applyAML(AML);

                    technical_log.Debug("Add Item,Revisions and files AML :" + AML);
                    if (Result.isError())
                    {
                        isSuccess = false;
                        isProcessSuccess = false;
                        Console.WriteLine("ERROR in GetAddItemAMLforType " + Result.getErrorString());
                        error_log.Error("Error while adding Part, Revision and Files" + itemId + "\n" + Result.getErrorString());

                        ite.setAction("edit");
                        ite.setProperty("cs_aml_status", "Failed");
                        ite.setProperty("cs_aml_details", Result.getErrorString());
                        Item itemStatusAml = ite.apply();

                    }
                    else
                    {
                        ite.setAction("edit");
                        ite.setProperty("cs_aml_status", "Success");
                        ite.setProperty("cs_aml_details", Result.getErrorString());
                        Item itemStatusAml = ite.apply();

                        if (String.IsNullOrEmpty(releaseStatus))
                        {
                            
                        }
                        else if(Result.getItemCount() >0)
                        {
                            String partState = Result.getProperty("state");

                            if (partState != releaseStatus)
                            {
                                String PromoteAML = "" +
                                "<AML>" +
                                    "<Item type='" + arasItemType + "' action='promoteItem' id='" + newArasID + "' > " +
                                        "<state>" + releaseStatus + "</state>" +
                                    "</Item>" +
                                "</AML>";

                                Item it1 = inn.newItem("CS_Transaction_Status_AML", "add");
                                it1.setProperty("source_id", TransactionStatusID);
                                it1.setProperty("cs_result", PromoteAML);
                                it1.setProperty("cs_aml_status", "Started");
                                Item ite1 = it1.apply();

                                try
                                {
                                    Item PromoteResult = inn.applyAML(PromoteAML);

                                    if (PromoteResult.isError())
                                    {
                                        isSuccess = false;
                                        isProcessSuccess = false;

                                        ite1.setAction("edit");
                                        ite1.setProperty("cs_aml_status", "Failed");
                                        ite1.setProperty("cs_aml_details", PromoteResult.getErrorString());
                                        Item itemStatusAml1 = ite1.apply();
                                    }
                                    else
                                    {
                                        ite1.setAction("edit");
                                        ite1.setProperty("cs_aml_status", "Success");
                                        Item itemStatusAml1 = ite1.apply();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    isSuccess = false;
                                    isProcessSuccess = false;

                                    ite1.setAction("edit");
                                    ite1.setProperty("cs_aml_status", "Failed");
                                    ite1.setProperty("cs_aml_details", ex.Message);
                                    Item itemStatusAml1 = ite1.apply();
                                }
                            }

                            //get CAD Document
                            Item partCADDocItem = inn.newItem("Part CAD", "get");
                            partCADDocItem.setProperty("source_id", newArasID);
                            partCADDocItem = partCADDocItem.apply();

                            //Result.fetchRelationships("Part CAD");

                            //Item partCADDocItem = Result.getRelationships("Part CAD");

                            for (int partcadindx = 0; partcadindx < partCADDocItem.getItemCount(); partcadindx++)
                            {
                                Item CADIem = partCADDocItem.getItemByIndex(partcadindx).getRelatedItem();

                                if (CADIem.getProperty("state") != releaseStatus)
                                {
                                    String PromoteAML = "" +
                                    "<AML>" +
                                        "<Item type='" + CADIem.getType() + "' action='promoteItem' id='" + CADIem.getID() + "' > " +
                                            "<state>" + releaseStatus + "</state>" +
                                        "</Item>" +
                                    "</AML>";

                                    Item it1 = inn.newItem("CS_Transaction_Status_AML", "add");
                                    it1.setProperty("source_id", TransactionStatusID);
                                    it1.setProperty("cs_result", PromoteAML);
                                    it1.setProperty("cs_aml_status", "Started");
                                    Item ite1 = it1.apply();

                                    try
                                    {
                                        Item PromoteResult = inn.applyAML(PromoteAML);

                                        if (PromoteResult.isError())
                                        {
                                            isSuccess = false;
                                            isProcessSuccess = false;

                                            ite1.setAction("edit");
                                            ite1.setProperty("cs_aml_status", "Failed");
                                            ite1.setProperty("cs_aml_details", PromoteResult.getErrorString());
                                            Item itemStatusAml1 = ite1.apply();
                                        }
                                        else
                                        {
                                            ite1.setAction("edit");
                                            ite1.setProperty("cs_aml_status", "Success");
                                            Item itemStatusAml1 = ite1.apply();
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        isSuccess = false;
                                        isProcessSuccess = false;

                                        ite1.setAction("edit");
                                        ite1.setProperty("cs_aml_status", "Failed");
                                        ite1.setProperty("cs_aml_details", ex.Message);
                                        Item itemStatusAml1 = ite1.apply();
                                    }
                                }
                            }

                        }

                    }
                    //Processing PLMXML Status Update
                    

                    xmlArasIDMapping.Add(revElementId, newArasID);
                }
                user_log.Info("Part Number added :" + itemId);

                //if (isSuccess)
                //{
                //    itmm.setAction("edit");
                //    itmm.setProperty("cs_transaction_status", "Success");
                //    itmm.apply();

                //}

            }

            return AML;
        }

        private static string getReleaseStatus(XmlNode itemRevtypeNode, string tcRevType, string status_ref_id, XmlDocument plmXMLDoc,out String releaseDate)
        {
            String Status = "";

            String ArasStateNAme = "";
            String ArasStateId = "";

            releaseDate = "";
            if(string.IsNullOrEmpty(status_ref_id))
            {
                return "";
            }

            String statusid = status_ref_id.Remove(0, 1);
            String relStatusXPath = "//ns:ReleaseStatus[@id='" + statusid + "']";
            XmlNode releaseStatusNode = plmXMLDoc.SelectSingleNode(relStatusXPath, oManager);

            String releaseStatusName = releaseStatusNode.Attributes["name"].Value;
            String releaseStatusDate = releaseStatusNode.Attributes["dateReleased"].Value;

            String stateXpath = "/Item/Type[@tc_RevisionType='" + tcRevType + "']/StateMapping/state[@tc_State_name='" + releaseStatusName + "']";

            XmlNode mappingDSNode = mappingXMLDoc.SelectSingleNode(stateXpath);
            if (mappingDSNode != null)
            {
                ArasStateNAme = mappingDSNode.Attributes["aras_state_name"].Value;
                ArasStateId = mappingDSNode.Attributes["aras_state_id"].Value;
                Status = ArasStateNAme;
                releaseDate = releaseStatusDate;
            }

            return Status;
        }

        private static string getNewRevisionID(string arasItemType, string newArasID)
        {
            String newID = null;
            Item Current_Item = inn.getItemById(arasItemType, newArasID);

            //Item NewRevision = Current_Item.setAction("Revise");
            // Version and unlock the item

            Item NewRevision = Current_Item.apply("version");
            if (!NewRevision.isError())
                NewRevision = NewRevision.apply("unlock");

            if (NewRevision != null && NewRevision.getItemCount() > 0)
            {
                newID = NewRevision.getItemByIndex(0).getID();

            }

            return newID;
        }

        private static string getArasID(string arasItemType, string itemId, string major_rev, out string itemAction)
        {
            String arasID = "";
            itemAction = "";

            String getItemSQL = "Select * from innovator.[" + arasItemType + "] where item_number = '" + itemId + "' order by MODIFIED_ON desc";

            //updateTechLog("getItemSQL --> " + getItemSQL);

            Item Result = inn.applySQL(getItemSQL);

            if (Result.isError())
            {
                isSuccess = false;
                Console.WriteLine("ERROR in getArasID :" + Result.getErrorString());
                error_log.Error("Error while executing SQL query" + Result.getErrorString());
                //updateLog("Exception while Queryng the Item" + Result.getErrorString());
                //is_error = true;
                //LineHasError = true;
            }

            //Check if the Part exist with Item number and revision

            Item Result_Items = Result.getItemsByXPath("//Result/Item[major_rev='" + major_rev + "']");

            //Update the Part if the revision Exist

            if (Result_Items.getItemCount() > 0)
            {
                itemAction = "merge";
                Item CurrRevision = Result_Items.getItemByIndex(0);
                arasID = CurrRevision.getProperty("id");
            }

            // Revise the part to get the required revision
            else if (Result.getItemCount() > 0 && Result_Items.getItemCount() <= 0)
            {
                itemAction = "version";
                Item CurrRevision = Result.getItemByIndex(0);
                arasID = CurrRevision.getProperty("id");
            }

            //Create the Part if the Item Number do not exist.

            else
            {
                itemAction = "merge";
                arasID = inn.getNewID();
            }

            return arasID;
        }

        private static string addCADAndOtherFiles(XmlNode itemRevtypeNode, string tcRevType, string tagTypeinXML, XmlDocument plmXMLDoc, string revElementId, string itemId, string major_rev, string releaseStatus, string releaseDate, string partID)
        {
            String addFilesAML = "";

            addFilesAML += getThubnailAML(itemRevtypeNode, tcRevType, tagTypeinXML, plmXMLDoc, revElementId);

            addFilesAML += getCADFilesAML(itemRevtypeNode, tcRevType, tagTypeinXML, plmXMLDoc, revElementId, itemId, major_rev, partID);

            return addFilesAML;
        }

        private static string getCADFilesAML(XmlNode itemRevtypeNode, string tcRevType, string tagTypeinXML, XmlDocument plmXMLDoc, string revElementId, string itemId, string major_rev, string partID)
        {
            String cadFilesAML = "";

            String nativeFileId = getNativeFileID(itemRevtypeNode, tcRevType, tagTypeinXML, plmXMLDoc, revElementId);
            List<String> getNonNativeFileIdList = getNonNativeFileIds(itemRevtypeNode, tcRevType, tagTypeinXML, plmXMLDoc, revElementId);
            String itemAction = "";
            String cadID = getArasID("CAD", itemId, major_rev, out itemAction);

            if (String.IsNullOrEmpty(itemAction) && String.IsNullOrEmpty(cadID))
            {
                return "";
            }

            if (itemAction == "version")
            {
                cadID = getNewRevisionID("CAD", cadID);

                if (String.IsNullOrEmpty(cadID))
                {
                    return "";
                }
                else
                {
                    itemAction = "merge";
                }

            }
            String partCADAction = "";

            String partCADId = getPartCADId(cadID, partID);

            cadFilesAML += "<Relationships>" +
                            "<Item type = 'Part CAD' action = 'merge' id='"+ partCADId + "'> " +
                             "<related_id>" +
                             "<Item type='CAD' action='" + itemAction + "' id='" + cadID + "'>" +
                             "<item_number>" + itemId + "</item_number>" +
                             "<major_rev>" + major_rev + "</major_rev>" +
                             "<native_file>" + nativeFileId + "</native_file>" +
                             "<Relationships>";

            foreach (String getNonNativeFileId in getNonNativeFileIdList)
            {
                cadFilesAML += "<Item type='CADFiles' action='add'>" +
                                "<attached_file>" + getNonNativeFileId + "</attached_file>" +
                              "</Item>";
                technical_log.Debug("Add Non-native files(PDF) AML :" + cadFilesAML);
            }

            cadFilesAML += "</Relationships>" +
                             "</Item>" +
                             "</related_id>" +
                             "</Item >" +
                             "</Relationships > ";


            return cadFilesAML;
        }

        private static string getPartCADId(string cadID, string partID)
        {
            String partcadid = "";

            Item partCADDocItem = inn.newItem("Part CAD", "get");
            partCADDocItem.setProperty("source_id", partID);
            partCADDocItem.setProperty("related_id", cadID);
            partCADDocItem = partCADDocItem.apply();

            if(partCADDocItem.getItemCount() > 0)
            {
                partcadid = partCADDocItem.getItemByIndex(0).getID();
            }
            else
            {
                partcadid = inn.getNewID();
            }

            return partcadid;
        }

        private static List<String> getNonNativeFileIds(XmlNode itemRevtypeNode, string tcRevType, string tagTypeinXML, XmlDocument plmXMLDoc, string revElementId)
        {
            List<String> nonNativeFilesIds = new List<string>();

            String dsXpath = "/Item/Type[@tc_RevisionType='" + tcRevType + "']/Files/DataSet[@in_cad='true' and @is_native='false']";

            XmlNodeList mappingDSNodeList = mappingXMLDoc.SelectNodes(dsXpath);
            foreach (XmlNode mappingDSNode in mappingDSNodeList)
            {
                String nonNativeFileformat = mappingDSNode.Attributes["format"].Value;

                if (!String.IsNullOrEmpty(nonNativeFileformat))
                {
                    String xpathforAssocDSNode = "//ns:" + tagTypeinXML + "[@id='" + revElementId + "']/ns:AssociatedDataSet";

                    XmlNodeList assocDSNodeList = itemRevtypeNode.SelectNodes(xpathforAssocDSNode, oManager);


                    foreach (XmlNode assocDSNode in assocDSNodeList)
                    {
                        String assocDSIdNum = assocDSNode.Attributes["dataSetRef"].Value;
                        String assocDSId = assocDSIdNum.Remove(0, 1);
                        //get the FormElementNode
                        String xpathforDSElementNode = "//ns:DataSet[@id='" + assocDSId + "' and @type='" + nonNativeFileformat + "']";

                        XmlNodeList DatasetNodeList = plmXMLDoc.SelectNodes(xpathforDSElementNode, oManager);
                        foreach (XmlNode DatasetNode in DatasetNodeList)
                        {
                            String NameRefIdNum = DatasetNode.Attributes["memberRefs"].Value;
                            String DatasetName = DatasetNode.Attributes["name"].Value;
                            String NameRefId = NameRefIdNum.Remove(0, 1);

                            String xpathforExtFileNode = "//ns:ExternalFile[@id='" + NameRefId + "']";

                            XmlNode ExtrFileNode = plmXMLDoc.SelectSingleNode(xpathforExtFileNode, oManager);
                            if (ExtrFileNode != null)
                            {
                                String dsFilePath = ExtrFileNode.Attributes["locationRef"].Value;
                                String enc = HttpUtility.UrlDecode(dsFilePath);
                                if (!String.IsNullOrEmpty(dsFilePath))
                                {
                                    String compfilePath = Path.Combine(pLMXMLFolderPath, enc);
                                    String FileName = Path.GetFileName(compfilePath);

                                    Item fileObj = inn.newItem("File", "add");
                                    fileObj.setProperty("filename", FileName);
                                    fileObj.attachPhysicalFile(compfilePath);
                                    fileObj = fileObj.apply();

                                    user_log.Info("PDF Document attached to part:" + FileName);
                                    if (fileObj.isError())
                                    {
                                        isSuccess = false;
                                        //is_error = true;
                                        //LineHasError = true;
                                        Console.WriteLine("ERROR in getNonNativeFileIds" + fileObj.getErrorString());
                                        error_log.Error("\t\tError while adding file '" + FileName + "'.." + fileObj.getErrorString());
                                        //throw new Exception();
                                    }
                                    nonNativeFilesIds.Add(fileObj.getID());
                                }
                            }
                        }
                    }
                }


            }

            return nonNativeFilesIds;
        }

        private static string getNativeFileID(XmlNode itemRevtypeNode, string tcRevType, string tagTypeinXML, XmlDocument plmXMLDoc, string revElementId)
        {
            String NativeFileId = "";

            String dsXpath = "/Item/Type[@tc_RevisionType='" + tcRevType + "']/Files/DataSet[@in_cad='true' and @is_native='true']";

            XmlNode mappingDSNode = mappingXMLDoc.SelectSingleNode(dsXpath);
            if (mappingDSNode != null)
            {
                String nativeFileformat = mappingDSNode.Attributes["format"].Value;

                if (!String.IsNullOrEmpty(nativeFileformat))
                {
                    String xpathforAssocDSNode = "//ns:" + tagTypeinXML + "[@id='" + revElementId + "']/ns:AssociatedDataSet";

                    XmlNodeList assocDSNodeList = itemRevtypeNode.SelectNodes(xpathforAssocDSNode, oManager);


                    foreach (XmlNode assocDSNode in assocDSNodeList)
                    {
                        String assocDSIdNum = assocDSNode.Attributes["dataSetRef"].Value;
                        String assocDSId = assocDSIdNum.Remove(0, 1);
                        //get the FormElementNode
                        String xpathforDSElementNode = "//ns:DataSet[@id='" + assocDSId + "' and @type='" + nativeFileformat + "']";

                        XmlNode DatasetNode = plmXMLDoc.SelectSingleNode(xpathforDSElementNode, oManager);
                        if (DatasetNode != null)
                        {
                            String NameRefIdNum = DatasetNode.Attributes["memberRefs"].Value;
                            String DatasetName = DatasetNode.Attributes["name"].Value;
                            String NameRefId = NameRefIdNum.Remove(0, 1);

                            String xpathforExtFileNode = "//ns:ExternalFile[@id='" + NameRefId + "']";

                            XmlNode ExtrFileNode = plmXMLDoc.SelectSingleNode(xpathforExtFileNode, oManager);
                            if (ExtrFileNode != null)
                            {
                                String dsFilePath = ExtrFileNode.Attributes["locationRef"].Value;
                                String enc = HttpUtility.UrlDecode(dsFilePath);
                                if (!String.IsNullOrEmpty(dsFilePath))
                                {
                                    String compfilePath = Path.Combine(pLMXMLFolderPath, enc);
                                    String FileName = Path.GetFileName(compfilePath);

                                    Item fileObj = inn.newItem("File", "add");
                                    fileObj.setProperty("filename", FileName);
                                    fileObj.attachPhysicalFile(compfilePath);
                                    fileObj = fileObj.apply();

                                    user_log.Info("CAD File attached to part:" + FileName);


                                    if (fileObj.isError())
                                    {

                                        //is_error = true;
                                        //LineHasError = true;
                                        isSuccess = false;
                                        Console.WriteLine("Error in getNativeFileID" + fileObj.getErrorString());
                                        error_log.Error("\t\tError while adding file '" + FileName + "'.." + fileObj.getErrorString());
                                        //throw new Exception();
                                    }
                                    NativeFileId = fileObj.getID();
                                }
                            }

                            break;
                        }
                    }
                }


            }

            return NativeFileId;
        }

        private static string getThubnailAML(XmlNode itemRevtypeNode, string tcRevType, string tagTypeinXML, XmlDocument plmXMLDoc, string revElementId)
        {
            String thumbnailAML = "";

            // get the Dataset Mapping
            String dsXpath = "/Item/Type[@tc_RevisionType='" + tcRevType + "']/Files/DataSet[@is_thubnail='true']";

            XmlNode mappingDSNode = mappingXMLDoc.SelectSingleNode(dsXpath);
            if (mappingDSNode != null)
            {
                String thubnailformat = mappingDSNode.Attributes["format"].Value;

                if (!String.IsNullOrEmpty(thubnailformat))
                {
                    //getDatasets of the Part...

                    String xpathforAssocDSNode = "//ns:" + tagTypeinXML + "[@id='" + revElementId + "']/ns:AssociatedDataSet";

                    XmlNodeList assocDSNodeList = itemRevtypeNode.SelectNodes(xpathforAssocDSNode, oManager);


                    foreach (XmlNode assocDSNode in assocDSNodeList)
                    {
                        String assocDSIdNum = assocDSNode.Attributes["dataSetRef"].Value;
                        String assocDSId = assocDSIdNum.Remove(0, 1);
                        //get the FormElementNode
                        String xpathforDSElementNode = "//ns:DataSet[@id='" + assocDSId + "' and @type='" + thubnailformat + "']";

                        XmlNode DatasetNode = plmXMLDoc.SelectSingleNode(xpathforDSElementNode, oManager);
                        if (DatasetNode != null)
                        {
                            String NameRefIdNum = DatasetNode.Attributes["memberRefs"].Value;
                            String DatasetName = DatasetNode.Attributes["name"].Value;
                            String NameRefId = NameRefIdNum.Remove(0, 1);

                            String xpathforExtFileNode = "//ns:ExternalFile[@id='" + NameRefId + "']";

                            XmlNode ExtrFileNode = plmXMLDoc.SelectSingleNode(xpathforExtFileNode, oManager);
                            if (ExtrFileNode != null)
                            {
                                String dsFilePath = ExtrFileNode.Attributes["locationRef"].Value;
                                String enc = HttpUtility.UrlDecode(dsFilePath);
                                if (!String.IsNullOrEmpty(dsFilePath))
                                {
                                    String compfilePath = Path.Combine(pLMXMLFolderPath, enc);
                                    String FileName = Path.GetFileName(compfilePath);

                                    Item fileObj = inn.newItem("File", "add");
                                    fileObj.setProperty("filename", FileName);
                                    fileObj.attachPhysicalFile(compfilePath);
                                    fileObj = fileObj.apply();

                                    user_log.Info("Thumbnails attached to part:" + FileName);

                                    if (fileObj.isError())
                                    {

                                        //is_error = true;
                                        //LineHasError = true;
                                        isSuccess = false;
                                        Console.WriteLine("getThubnailAML" + FileName + fileObj.getErrorString());
                                        error_log.Error("\t\tError while adding file '" + FileName + "'.." + fileObj.getErrorString());

                                        //throw new Exception();
                                    }
                                    string fileid = fileObj.getID();
                                    thumbnailAML += "<thumbnail>vault:///?fileId=" + fileid + "</thumbnail>";
                                    technical_log.Debug("Add Thumbnails AML :" + thumbnailAML);
                                }
                            }

                            break;
                        }
                    }

                }
            }

            return thumbnailAML;
        }

        private static string addItemRevProperties(XmlNode itemRevtypeNode, string tcRevType, string tagTypeinXML, XmlDocument plmXMLDoc, string revElementId)
        {
            String itemRevPropertiesAML = "";

            String propXpath = "/Item/Type[@tc_RevisionType='" + tcRevType + "']/Properties[@tc_type='Revision']";

            XmlNodeList mappingPropList = mappingXMLDoc.SelectNodes(propXpath);

            foreach (XmlNode mappingProp in mappingPropList)
            {
                String xpathforPropNode = "";
                String propPlace = mappingProp.Attributes["tc_prop_place"].Value;
                String tc_prop = mappingProp.Attributes["tc_prop"].Value;
                String aras_prop = mappingProp.Attributes["aras_prop"].Value;

                if (propPlace == "UserData")
                {
                    xpathforPropNode = "//ns:" + tagTypeinXML + "[@id='" + revElementId + "']/ns:UserData/ns:UserValue[@title='" + tc_prop + "']";

                    XmlNode propNode = itemRevtypeNode.SelectSingleNode(xpathforPropNode, oManager);
                    if (propNode == null)
                    {
                        continue;
                    }
                    String propValue = propNode.Attributes["value"].Value;
                    if (!string.IsNullOrEmpty(propValue))
                    {
                        itemRevPropertiesAML += "<" + aras_prop + ">" + propValue + "</" + aras_prop + ">";
                    }

                }
                else if (propPlace == tagTypeinXML)
                {
                    xpathforPropNode = "//ns:" + tagTypeinXML + "[@id='" + revElementId + "']/ns:" + tc_prop + "";

                    XmlNode propNode = itemRevtypeNode.SelectSingleNode(xpathforPropNode, oManager);
                    if (propNode == null)
                    {
                        continue;
                    }
                    String propValue = propNode.InnerText;

                    if (!string.IsNullOrEmpty(propValue))
                    {
                        itemRevPropertiesAML += "<" + aras_prop + ">" + propValue + "</" + aras_prop + ">";
                    }

                }
                else if (propPlace == "Form")
                {
                    itemRevPropertiesAML += getFormAttributes(plmXMLDoc, itemRevtypeNode, tagTypeinXML, revElementId, tc_prop, aras_prop);
                }

            }

            return itemRevPropertiesAML;
        }

        private static string getFormAttributes(XmlDocument plmXMLDoc, XmlNode itemRevtypeNode, string tagTypeinXML, string revElementId, string tc_prop, string aras_prop)
        {
            String formPropAML = "";

            //get formID 
            String xpathforAssocFormNode = "//ns:" + tagTypeinXML + "[@id='" + revElementId + "']/ns:AssociatedForm";

            XmlNode assocFormNode = itemRevtypeNode.SelectSingleNode(xpathforAssocFormNode, oManager);

            if (assocFormNode != null)
            {
                String assocFormIdNum = assocFormNode.Attributes["formRef"].Value;
                String assocFormId = assocFormIdNum.Remove(0, 1);
                //get the FormElementNode
                String xpathforFormElementNode = "//ns:Form[@id='" + assocFormId + "']/ns:UserData/ns:UserValue[@title='" + tc_prop + "']";

                XmlNode FormpropNode = plmXMLDoc.SelectSingleNode(xpathforFormElementNode, oManager);
                if (FormpropNode != null)
                {
                    String propValue = FormpropNode.Attributes["value"].Value; ;

                    if (!string.IsNullOrEmpty(propValue))
                    {
                        formPropAML += "<" + aras_prop + ">" + propValue + "</" + aras_prop + ">";
                    }
                }
            }

            return formPropAML;
        }

        private static string addItemProperties(XmlNode itemtypeNode, string tcItemTtpe)
        {
            String itemPropertiesAML = "";

            String propXpath = "/Item/Type[@tc_ItemType='" + tcItemTtpe + "']/Properties[@tc_type='Item']";
            //mappingXMLDoc


            return itemPropertiesAML;
        }

        private static void ProcessMappingXML()
        {
            XmlNodeList TcPartTypesList = mappingXMLDoc.SelectNodes("/Item/Type");
            foreach (XmlNode TcPAthType in TcPartTypesList)
            {
                String tcItemtype = TcPAthType.Attributes["tc_ItemType"].Value;
                String tcRevtype = TcPAthType.Attributes["tc_RevisionType"].Value;
                String arasClass = TcPAthType.Attributes["aras_Class"].Value;
                String itemIdAtt = TcPAthType.Attributes["itemIdAtt"].Value;
                String tagTypeinXML = TcPAthType.Attributes["tagTypeinXML"].Value;
                String arasItem = TcPAthType.Attributes["arasItem"].Value;

                PartTypeList.Add(new partTypes(tcItemtype, tcRevtype, arasClass, itemIdAtt, tagTypeinXML, arasItem));

                //get Item Properties
            }
        }

        private static void promoteRespectiveWF()
        {
            if (changeNumber == "" && changeType == "")
            {

            }
            else
            {
                //get the ChangeItem by KeyedName
                Item itm = inn.getItemByKeyedName(changeType, changeNumber);
                //get id
                if(itm.getItemCount() <= 0)
                {
                    return;
                }

                string id = itm.getID();
                //get WkFlowProcess id
                Item getWfItem = inn.newItem("Workflow", "get");

                if (isSuccess)
                {
                    string xmlStr = "<itemId>" + id + "</itemId><wfpid>" + getWfItem.getProperty("related_id") + "</wfpid><itemType>" + changeType + "</itemType><votingPath>Review Design</votingPath>";
                    Item wfPromot = inn.applyMethod("CS_WorkflowAutoPromote", "<body>" + xmlStr + "</body>");
                }
                else
                {
                    string xmlStr = "<itemId>" + id + "</itemId><wfpid>" + getWfItem.getProperty("related_id") + "</wfpid><itemType>" + changeType + "</itemType><votingPath>Failed</votingPath>";
                    Item wfPromot = inn.applyMethod("CS_WorkflowAutoPromote", "<body>" + xmlStr + "</body>");
                }
                // string xmlStr = "<itemId>" + changeNumber + "</itemId><wfpid>" + getWfItem.getProperty("related_id") + "</wfpid><itemType>"+changeType+"</itemType><votingPath>Failed</votingPath>";
                //Item wfPromot = inn.applyMethod("CS_WorkflowAutoPromote", "<body>" + xmlStr + "</body>");
            }

        }

        private static void deletePLMXMLFolder()
        {
            if (isSuccess)
            {

                Item ts_Item = inn.newItem("CS_Transaction_Status", "add");
                ts_Item.setProperty("source_id", TransactionLogID);
                ts_Item.setProperty("cs_transaction_path", "Deleting PLMXML");
                ts_Item.setProperty("cs_transaction_action", "deleted");
                ts_Item.setProperty("cs_transaction_time", DateTime.Now.ToString());
                ts_Item.setProperty("cs_transaction_status", "Started");
                ts_Item.setProperty("cs_transaction_user", ConfigurationManager.AppSettings["userid"]);
                ts_Item.setProperty("cs_details", pLMXMLFileName + '\t' + "is processing now.");

                Item ts_ItemResult = ts_Item.apply();
                TransactionStatusID = ts_ItemResult.getID();

                try
                {
                    string FolderPath = pLMXMLFolderPath;
                    Directory.Delete(FolderPath, true);

                    ts_ItemResult.setAction("edit");
                    ts_ItemResult.setProperty("cs_transaction_status", "Success");
                    ts_ItemResult = ts_ItemResult.apply();
                }
                catch (Exception ex)
                {
                    ts_ItemResult.setAction("edit");
                    ts_ItemResult.setProperty("cs_transaction_status", "Faied");
                    ts_ItemResult = ts_ItemResult.apply();
                }

                
            }
        }

        public class partTypes
        {
            public string tcItemType = "";
            public string tcRevType = "";
            public string arasClass = "";
            public string itemIdAtt = "";
            public string tagTypeinXML = "";
            public string arasItemType = "";

            public partTypes(String _tcItemtype, String _tcRevtype, String _arasClass, String _itemIdAtt, String _tagTypeinXML, String _arasItem)
            {
                tcItemType = _tcItemtype;
                tcRevType = _tcRevtype;
                arasClass = _arasClass;
                itemIdAtt = _itemIdAtt;
                tagTypeinXML = _tagTypeinXML;
                arasItemType = _arasItem;
            }
        }

        private static void ImplementLoggingFuntion()
        {
            error_log.Error("***********ERROR LOG_FILE********");
            user_log.Info("*********USER LOG_FILE*******");
            technical_log.Debug("**************TECHNICAL LOG_FILE************");
        }
    }
}
