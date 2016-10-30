using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNet.SignalR;
using System.Threading.Tasks;
using OrdreOversigt.Models;
using System.Data.Entity;
using Microsoft.AspNet.SignalR.Hubs;
using System.Data;

namespace OrdreOversigt.Hubs
{
    //Real-time OrdreOversigt med SignalR
    public class OrdersViewHub : Hub
    {
        //Der laves en ny "Instance" af en liste af Ordre-Lister til hvert View der eksistere i systemet.
        public static List<List> viewProduction = new List<List>();
        public static List<List> viewGraphics = new List<List>();
        public static List<List> viewBilling = new List<List>();

        //Metode bliver kaldt fra en .js fil der sender navnet på det View som brugeren er på med som parameter.
        public void getView(string viewName)
        {
            var task = Task.Factory.StartNew(() =>
               {
                   using (var context = new OrdreOversigtMVCEntities())
                   {
                       switch (viewName)
                       {
                           //Alt efter hvilken View man er inde på kommer der et forskelligt "viewName", dette er for at brugere der er på fx. "Billing" ikke får vist Ordre der er tilegnet "Production".
                           case "Production":
                               //Listen af Ordre-Lister der tilhøre brugerens View får data baseret på dets navn.
                               //Den nuværende dataContext bliver sendt med som parameter da den skal fungere som var den statisk, 
                               //lige ledes bliver navnet på Viewet sendt med da det bliver brugt til at filtrere listerne i databasen.
                               viewProduction = genListViewModel(context, "Production");
                               //Brugeren tilføjes til en gruppe der tilhøre det View brugeren er på.
                               //Hvis gruppen ikke eksistere bliver den automatisk oprettet.
                               Groups.Add(Context.ConnectionId, "Production");
                               //Gruppens "recViewLists" (recived View Lists) bliver sat til de lister der tilhøre gruppen og bliver sorteret efter et Index så man i admin kan sortere dem som man lyster
                               //recViewLists bliver brugt i .js filen til at alle lister der tilhøre Viewet bliver vist ved at manipulere DOM'en Client-side
                               Clients.Group("Production").recViewLists(viewProduction.OrderBy(p => p.ListIndex));
                               break;
                           case "Graphic":
                               viewGraphics = genListViewModel(context, "Graphic");
                               Groups.Add(Context.ConnectionId, "Graphic");
                               Clients.Group("Graphic").recViewLists(viewGraphics.OrderBy(p => p.ListIndex));
                               break;
                           case "Billing":
                               viewBilling = genListViewModel(context, "Billing");
                               Groups.Add(Context.ConnectionId, "Billing");
                               Clients.Group("Billing").recViewLists(viewBilling.OrderBy(p => p.ListIndex));
                               break;
                           default:
                               break;
                       }
                   }
               }, TaskCreationOptions.LongRunning);
        }

        public static List<List> genListViewModel(OrdreOversigtMVCEntities listDB, String listType)
        {
            //Der laves en ny "Instance" af en liste af Ordre-Lister som alle data kan sendes i inden de returneres.
            List<List> model = new List<List>();

            //Henter alle lister der har samme type navn som det "View" man er inde på.
            foreach (var item in listDB.Lists.Where(p => p.Type == listType))
            {
                //Hvis listen har underlister bliver de hentet.
                if (item.SubLists_List.Count >= 1)
                {
                    foreach (var collectionSubList in item.SubLists_List)
                    {
                        //Sætter Underlistens liste til "null" da "KnockoutJS" ikke kan håndtere de loops der er i dataene
                        //Denne form for at fjerne loop data bliver brugt løbende i projektet.
                        collectionSubList.List_List = null;
                        collectionSubList.List_SubList.SubLists_SubList = null;
                        List sublist = listDB.Lists.Where(p => p.ID == collectionSubList.FK_SubID).FirstOrDefault();

                        //Henter Ordre der tilhøre listen.
                        sublist.OrdersLinks = getOrders(listDB, sublist);
                        sublist.SubLists_SubList = null;
                    }
                }
                //Henter Ordre der tilhøre listen.
                item.OrdersLinks = getOrders(listDB, item);

                //Liste og dets underlister bliver tilføjet til lister brugeren får vist.
                model.Add(item);
            }

            //Henter alle Globale lister og tilføjer dem til de lister brugeren skal have vist.
            foreach (var item in listDB.Lists.Where(p => p.Type == "Global"))
            {
                if (item.SubLists_List.Count >= 1)
                {
                    foreach (var collectionSubList in item.SubLists_List)
                    {
                        collectionSubList.List_List = null;
                        collectionSubList.List_SubList.SubLists_SubList = null;
                        List sublist = listDB.Lists.Where(p => p.ID == collectionSubList.FK_SubID).FirstOrDefault();

                        sublist.OrdersLinks = getOrders(listDB, sublist);
                        sublist.SubLists_SubList = null;
                    }
                }
                item.OrdersLinks = getOrders(listDB, item);
                if (listType == "Billing")
                {
                    if (item.ID != 71)
                    {
                        //Fix for at en enkelt liste ikke bliver vist på "Billing"
                        //Efter ønske fra chefen skulle en enkelt liste ikke blive vist på "Billing"
                        //Bliver løst ved at ekskludere ID'et for listen, dette er ikke et problem da listen kun kan slettes direkte i databasen og ikke i systemet.
                        model.Add(item);
                    }
                }
                else
                {
                    model.Add(item);
                }
            }
            return model;
        }

        public static ICollection<OrdersLink> getOrders(OrdreOversigtMVCEntities listDB, List list)
        {
            //Laver en ny "Instance" af en samling af Ordre.
            ICollection<OrdersLink> orders = new List<OrdersLink>();

            //Data for alle Ordre der tilhøre listen.
            foreach (var order in listDB.OrdersLinks.Where(p => p.FK_ListID == list.ID).OrderBy(p => p.OrderIndex))
            {
                order.List = null;
                //Hvis Ordren er en "Kliche" (tryk plade) bliver der vist "Stops" så det er nemere at se hvor langt "Klichen" er i produktionen.
                if (order.Order_Stop_Link.Count >= 1)
                {
                    List<Order_Stop_Link> colOSL = new List<Order_Stop_Link>();
                    foreach (var osl in order.Order_Stop_Link)
                    {
                        osl.OrdersLink = null;
                        osl.Stop.Order_Stop_Link.Clear();
                        colOSL.Add(osl);
                    }
                    order.Order_Stop_Link = colOSL.OrderBy(p => p.ID).ToList();
                }
                if (order.OrdersInfoes.Count > 0)
                {
                    order.OrdersInfoes.FirstOrDefault().OrdersLink = null;
                }
                orders.Add(order);
            }
            return orders;
        }
    }
}