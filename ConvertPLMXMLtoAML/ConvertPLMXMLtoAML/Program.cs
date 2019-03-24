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

namespace ConvertPLMXMLtoAML
{
    class Program
    {
        static List<partTypes> PartTypeList = new List<partTypes>();
        static Hashtable xmlArasIDMapping = new Hashtable();

        static XmlNamespaceManager oManager = new XmlNamespaceManager(new NameTable());

        static HttpServerConnection conn = null;
        static Innovator inn = null;


        static void Main(string[] args)
        {
            logintoAras();

            if (conn == null)
            {
                Console.WriteLine("Error while connecting to Aras.");
                return;
            }

            oManager.AddNamespace("ns", "http://www.plmxml.org/Schemas/PLMXMLSchema");

            XmlDocument mappingXMLDoc = new XmlDocument();
            String pLMXMLFolderPath = @"C:\Chaitanya\Offical\Projects\Magna\Development\PLMXML-AML\Sample from George\Export";
            String pLMXMLFileName = "ASP90000001_AA01_1-Name_01_Cosma4.xml";

            mappingXMLDoc.Load(@"C:\Users\Chaitanya P\Documents\GitHub\Aras\ConvertPLMXMLTOAML\ConvertPLMXMLtoAML\ConvertPLMXMLtoAML\TCtoArasMapping.xml");

            //get the Part Types in Mapping Sheet 
            ProcessMappingXML(mappingXMLDoc);


            ProcessPLMXML(pLMXMLFolderPath, pLMXMLFileName);

            if (conn != null)
            {
                conn.Logout();
            }
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
            if (login_result.isError())
            {
                //return null;
                //MessageBox.Show(login_result.getErrorString().Replace("SOAP-ENV:ServerAuthentication failed for admin", ""), "Login Failed");
                return null;
                //throw new Exception("Login failed :-" + login_result.getErrorString());
            }

            return connect;
        }


        private static void ProcessPLMXML(string pLMXMLFolderPath, string pLMXMLFileName)
        {
            StringBuilder finalAMLScript = new StringBuilder();
            StringBuilder addItemsScript = new StringBuilder();
            StringBuilder addBOMScript = new StringBuilder();

            String pLMXMLFilePath = Path.Combine(pLMXMLFolderPath, pLMXMLFileName);

            XmlDocument plmXMLDoc = new XmlDocument();

            plmXMLDoc.Load(pLMXMLFilePath);

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


            Console.Write(finalAMLScript);
        }

        private static String getBOMAML(XmlDocument plmXMLDoc)
        {
            String bomAML = "";

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

                    Item BOMResult = inn.applyAML(bomAML);


                }

            }

            return bomAML;
        }

        private static string getBOMPropertiesAML(string parentxmlID, string childxmlID)
        {
            String bompropaml = "";
            return bompropaml;
        }
        static int partid = 0;
        private static string GetAddItemAMLforType(XmlDocument plmXMLDoc, partTypes partType)
        {
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


                string itemPropAML = addItemProperties(ItemtypeNode);

                //read Revision Elements
                String revXPath = "//ns:" + tagTypeinXML + "Revision[@subType='" + tcRevType + "' and @masterRef='#" + itemElementID + "']";
                XmlNodeList ItemRevtypeNodeList = plmXMLDoc.SelectNodes(revXPath, oManager);


                foreach (XmlNode ItemRevtypeNode in ItemRevtypeNodeList)
                {
                    String major_rev = ItemRevtypeNode.Attributes["revision"].Value;
                    String revElementId = ItemRevtypeNode.Attributes["id"].Value;
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

                    string itemRevPropAML = addItemRevProperties(ItemRevtypeNode);
                    string addFilesAML = addCADAndOtherFiles(ItemRevtypeNode);

                    AML = "<AML><Item type='" + arasItemType + "' action='merge' id='" + newArasID + "'>" +
                        "<item_number>" + itemId + "</item_number>" +
                        "<major_rev>" + major_rev + "</major_rev>" +
                        "<name>" + itemName + "</name>" +
                        itemPropAML +
                        itemRevPropAML +
                        "</Item></AML>";
                    Item Result = inn.applyAML(AML);

                    xmlArasIDMapping.Add(revElementId, newArasID);
                }
            }
            return AML;
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

        private static string addCADAndOtherFiles(XmlNode itemRevtypeNode)
        {
            String addFilesAML = "";


            return addFilesAML;
        }

        private static string addItemRevProperties(XmlNode itemRevtypeNode)
        {
            String itemRevPropertiesAML = "";


            return itemRevPropertiesAML;
        }

        private static string addItemProperties(XmlNode itemtypeNode)
        {
            String itemPropertiesAML = "";

            return itemPropertiesAML;
        }

        private static void ProcessMappingXML(XmlDocument mappingXMLDoc)
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
}
