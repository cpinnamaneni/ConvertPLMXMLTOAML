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
        static String pLMXMLFolderPath = "";

        static XmlDocument mappingXMLDoc = new XmlDocument();
        static List<partTypes> PartTypeList = new List<partTypes>();
        static Hashtable xmlArasIDMapping = new Hashtable();

        static Hashtable itemPropertyMapping = new Hashtable();
        static Hashtable itemRevPropertyMapping = new Hashtable();

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

            
            pLMXMLFolderPath = @"C:\Chaitanya\Offical\Projects\Magna\Development\PLMXML-AML\Sample from George\Export";
            String pLMXMLFileName = "ASP90000001_AA01_1-Name_01_Cosma4.xml";

            mappingXMLDoc.Load(@".\TCtoArasMapping.xml");
            //mappingXMLDoc.Load(@"C:\Users\Chaitanya P\Documents\GitHub\Aras\ConvertPLMXMLTOAML\ConvertPLMXMLtoAML\ConvertPLMXMLtoAML\TCtoArasMapping.xml");

            //get the Part Types in Mapping Sheet 
            ProcessMappingXML();


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


                string itemPropAML = addItemProperties(ItemtypeNode, tcItemTtpe);

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

                    string itemRevPropAML = addItemRevProperties(ItemRevtypeNode, tcRevType, tagTypeinXML + "Revision", plmXMLDoc, revElementId);
                    string addFilesAML = addCADAndOtherFiles(ItemRevtypeNode, tcRevType, tagTypeinXML + "Revision", plmXMLDoc, revElementId, itemId, major_rev);

                    AML = "<AML><Item type='" + arasItemType + "' action='merge' id='" + newArasID + "'>" +
                        "<item_number>" + itemId + "</item_number>" +
                        "<major_rev>" + major_rev + "</major_rev>" +
                        "<name>" + itemName + "</name>" +
                        itemPropAML +
                        itemRevPropAML +
                        addFilesAML +
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

        private static string addCADAndOtherFiles(XmlNode itemRevtypeNode, string tcRevType, string tagTypeinXML, XmlDocument plmXMLDoc, string revElementId, string itemId, string major_rev)
        {
            String addFilesAML = "";

            addFilesAML += getThubnailAML(itemRevtypeNode, tcRevType, tagTypeinXML , plmXMLDoc, revElementId); 

            addFilesAML += getCADFilesAML(itemRevtypeNode, tcRevType, tagTypeinXML, plmXMLDoc, revElementId, itemId, major_rev);

            return addFilesAML;
        }

        private static string getCADFilesAML(XmlNode itemRevtypeNode, string tcRevType, string tagTypeinXML, XmlDocument plmXMLDoc, string revElementId, string itemId, string major_rev)
        {
            String cadFilesAML = "";
            //
            String nativeFileId = getNativeFileID(itemRevtypeNode, tcRevType, tagTypeinXML, plmXMLDoc, revElementId);
            List <String> getNonNativeFileIdList = getNonNativeFileIds(itemRevtypeNode, tcRevType, tagTypeinXML, plmXMLDoc, revElementId);
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

            cadFilesAML += "<Relationships>" +
                            "<Item type = 'Part CAD' action = 'add' > " +
                             "<related_id>" +
                             "<Item type='CAD' action='" + itemAction + "' id='" + cadID + "'>" +
                             "<item_number>" + itemId + "</item_number>" +
                             "<major_rev>" + major_rev + "</major_rev>" +
                             "<native_file>" + nativeFileId + "</native_file>" +
                             "<Relationships>";
                             
            foreach(String getNonNativeFileId in getNonNativeFileIdList)
            {
                cadFilesAML += "<Item type='CADFiles' action='add'>" +
                                "<attached_file>" + getNonNativeFileId + "</attached_file>" +
                              "</Item>";
            }

            cadFilesAML += "</Relationships>" +
                             "</Item>" +
                             "</related_id>" +
                             "</Item >" +
                             "</Relationships > ";

            return cadFilesAML;
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
                                if (!String.IsNullOrEmpty(dsFilePath))
                                {
                                    String compfilePath = Path.Combine(pLMXMLFolderPath, dsFilePath);
                                    String FileName = Path.GetFileName(compfilePath);

                                    Item fileObj = inn.newItem("File", "add");
                                    fileObj.setProperty("filename", FileName);
                                    fileObj.attachPhysicalFile(compfilePath);
                                    fileObj = fileObj.apply();
                                    if (fileObj.isError())
                                    {

                                        //is_error = true;
                                        //LineHasError = true;
                                        //updateLog("\t\tError while adding file '" + realFileName + "'.." + fileObj.getErrorString());

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
            if(mappingDSNode != null)
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
                                if (!String.IsNullOrEmpty(dsFilePath))
                                {
                                    String compfilePath = Path.Combine(pLMXMLFolderPath, dsFilePath);
                                    String FileName = Path.GetFileName(compfilePath);

                                    Item fileObj = inn.newItem("File", "add");
                                    fileObj.setProperty("filename", FileName);
                                    fileObj.attachPhysicalFile(compfilePath);
                                    fileObj = fileObj.apply();
                                    if (fileObj.isError())
                                    {

                                        //is_error = true;
                                        //LineHasError = true;
                                        //updateLog("\t\tError while adding file '" + realFileName + "'.." + fileObj.getErrorString());

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
            if(mappingDSNode != null)
            {
                String thubnailformat = mappingDSNode.Attributes["format"].Value;

                if(!String.IsNullOrEmpty(thubnailformat))
                {
                    //getDatasets of the Part...

                    String xpathforAssocDSNode = "//ns:" + tagTypeinXML + "[@id='" + revElementId + "']/ns:AssociatedDataSet";

                    XmlNodeList assocDSNodeList = itemRevtypeNode.SelectNodes(xpathforAssocDSNode, oManager);


                    foreach (XmlNode assocDSNode in assocDSNodeList)
                    {
                        String assocDSIdNum = assocDSNode.Attributes["dataSetRef"].Value;
                        String assocDSId = assocDSIdNum.Remove(0, 1);
                        //get the FormElementNode
                        String xpathforDSElementNode = "//ns:DataSet[@id='" + assocDSId + "' and @type='"+ thubnailformat + "']";

                        XmlNode DatasetNode = plmXMLDoc.SelectSingleNode(xpathforDSElementNode, oManager);
                        if (DatasetNode != null)
                        {
                            String NameRefIdNum = DatasetNode.Attributes["memberRefs"].Value;
                            String DatasetName = DatasetNode.Attributes["name"].Value;
                            String NameRefId = NameRefIdNum.Remove(0,1);

                            String xpathforExtFileNode = "//ns:ExternalFile[@id='" + NameRefId + "']";

                            XmlNode ExtrFileNode = plmXMLDoc.SelectSingleNode(xpathforExtFileNode, oManager);
                            if(ExtrFileNode != null)
                            {
                                String dsFilePath = ExtrFileNode.Attributes["locationRef"].Value;
                                if(!String.IsNullOrEmpty(dsFilePath))
                                {
                                    String compfilePath = Path.Combine(pLMXMLFolderPath, dsFilePath);
                                    String FileName = Path.GetFileName(compfilePath);

                                    Item fileObj = inn.newItem("File", "add");
                                    fileObj.setProperty("filename", FileName);
                                    fileObj.attachPhysicalFile(compfilePath);
                                    fileObj = fileObj.apply();
                                    if (fileObj.isError())
                                    {

                                        //is_error = true;
                                        //LineHasError = true;
                                        //updateLog("\t\tError while adding file '" + realFileName + "'.." + fileObj.getErrorString());

                                        //throw new Exception();
                                    }
                                    string fileid = fileObj.getID();
                                    thumbnailAML += "<thumbnail>vault:///?fileId=" + fileid + "</thumbnail>";
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

            foreach(XmlNode mappingProp in mappingPropList)
            {
                String xpathforPropNode = "";
                String propPlace = mappingProp.Attributes["tc_prop_place"].Value;
                String tc_prop = mappingProp.Attributes["tc_prop"].Value;
                String aras_prop = mappingProp.Attributes["aras_prop"].Value;

                if (propPlace == "UserData")
                {
                    xpathforPropNode = "//ns:"+ tagTypeinXML + "[@id='"+ revElementId + "']/ns:UserData/ns:UserValue[@title='"+ tc_prop + "']";

                    XmlNode propNode = itemRevtypeNode.SelectSingleNode(xpathforPropNode,oManager);
                    if (propNode == null)
                    {
                        continue;
                    }
                    String propValue = propNode.Attributes["value"].Value;
                    if(!string.IsNullOrEmpty(propValue))
                    {
                        itemRevPropertiesAML += "<" + aras_prop + ">" + propValue + "</" + aras_prop + ">";
                    }

                }
                else if (propPlace == tagTypeinXML)
                {
                    xpathforPropNode = "//ns:" + tagTypeinXML + "[@id='" + revElementId + "']/ns:" + tc_prop + "";

                    XmlNode propNode = itemRevtypeNode.SelectSingleNode(xpathforPropNode, oManager);
                    if(propNode == null)
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
                if(FormpropNode != null)
                {
                    String propValue = FormpropNode.Attributes["value"].Value;;

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
