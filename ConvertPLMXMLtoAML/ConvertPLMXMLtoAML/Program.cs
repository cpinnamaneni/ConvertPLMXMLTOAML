using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Aras;

namespace ConvertPLMXMLtoAML
{
    class Program
    {
        static List<partTypes> PartTypeList = new List<partTypes>();
        static Hashtable xmlArasIDMapping = new Hashtable();

        static XmlNamespaceManager oManager = new XmlNamespaceManager(new NameTable());
       

        static void Main(string[] args)
        {
            oManager.AddNamespace("ns", "http://www.plmxml.org/Schemas/PLMXMLSchema");

            XmlDocument mappingXMLDoc = new XmlDocument();
            String pLMXMLFolderPath = @"C:\Chaitanya\Offical\Projects\Magna\Development\PLMXML-AML\Sample from Prashanth";
            String pLMXMLFileName = "000058_A_1-Auto_Product_Main.xml";

            mappingXMLDoc.Load(@"C:\Users\Chaitanya P\source\repos\ConvertPLMXML-AML\ConvertPLMXMLtoAML\ConvertPLMXMLtoAML\TCtoArasMapping.xml");

            //get the Part Types in Mapping Sheet 
            ProcessMappingXML(mappingXMLDoc);

            
            ProcessPLMXML(pLMXMLFolderPath, pLMXMLFileName);
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

            foreach(XmlNode occuranceNode in occuranceNodeList)
            {

                //get the Parent ID
                String parentIDNum = occuranceNode.Attributes["instancedRef"].Value;
                String parentxmlID = parentIDNum.Remove(0, 1);
                String parentArasID = xmlArasIDMapping[parentxmlID].ToString();

                //get Child IDs
                XmlAttribute occrefAttr = occuranceNode.Attributes["occurrenceRefs"];
                if(occrefAttr == null)
                {
                    continue;
                }
                String childIDs = occrefAttr.Value;
                string[] childIDArray = childIDs.Split(' ');

                foreach(String childOccxmlID in childIDArray)
                {
                    XmlNode childOccNode = plmXMLDoc.SelectSingleNode("//ns:Occurrence[@id='"+childOccxmlID+"']",oManager);
                    string childxmlIDNum = childOccNode.Attributes["instancedRef"].Value;
                    string childxmlID = childxmlIDNum.Remove(0, 1);
                    String childItemArasId = xmlArasIDMapping[childxmlID].ToString();
                    string bomPropAML = getBOMPropertiesAML(parentxmlID,childxmlID);

                    bomAML += "<Item type='Part BOM' action='add'>" +
                        "<source_id>" + parentArasID + "</source_id>" +
                        "<related_id>" + childItemArasId + "<related_id>" +
                        bomPropAML +
                        "<AML>";


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

            foreach(XmlNode ItemtypeNode in ItemtypeNodeList)
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
                    String newArasID = ++partid+"0000";//getArasID(arasItemType,itemId,major_rev);


                    xmlArasIDMapping.Add(revElementId, newArasID);

                    string itemRevPropAML = addItemRevProperties(ItemRevtypeNode);
                    string addFilesAML = addCADAndOtherFiles(ItemRevtypeNode);

                    AML += "<Item type='" + arasItemType + "' action='add' id='" + newArasID + "'>" +
                        "<item_number>" + itemId + "</item_number>" +
                        "<major_rev>" + major_rev + "</major_rev>" +
                        "<name>" + itemName + "</name>" +
                        itemPropAML +
                        itemRevPropAML+
                        "</Item>";

                }
            }
            return AML;
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

        public partTypes(String _tcItemtype, String _tcRevtype,String _arasClass,String _itemIdAtt,String _tagTypeinXML,String _arasItem)
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
