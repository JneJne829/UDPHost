using System;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PlayerDataNamespace;
using HostDataNamespace;
using ClientDataNamespace;
using PlayerPointNamespace;
using System.Collections.Concurrent;
using FoodNamespace;
using System.Drawing;


public class UdpServer
{
    private static HashSet<IPEndPoint> clientEndpoints = new HashSet<IPEndPoint>();
    private static ConcurrentDictionary<int, PlayerData> playerList = new ConcurrentDictionary<int, PlayerData>();
    private static IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
    private static UdpClient udpListener = new UdpClient(11000);
    private static HostData hostData = new(0, new Content("", 0));
    private static List<Food> foods = new List<Food>();
    private static Random random = new Random();
    private static DateTime lastPrintTime = DateTime.MinValue;
    private static DateTime lastPrintTime2 = DateTime.MinValue;
    private static DateTime timeNow = DateTime.Now;
    private static DateTime setTime;
    private static Point map = new Point(4000,2000);
    private const double MinimumMovementThreshold = 5.0;
    private static double MouseSpeed = 3;
    private const int initFood = 1000;
    private const int maxFood = 2000;
    private static int ellipseMapPointer = 0;
    private static bool[,] grid = new bool[map.X, map.Y];
    private static bool IsCreatFood = false;
    private static bool loop = true;
    private static bool generateFoodLock = false;
   
    

    public static void Main()
    {
        var clientData = new ClientData {Mode = 0, Number = 1, Position = new PlayerPoint(100, 200) };
        string json = JsonSerializer.Serialize(clientData);
        var deserializedClientData = JsonSerializer.Deserialize<ClientData>(json);
        StartUdpListener();
        GameComputing();
        while (loop) // loop 是一个静态字段，控制程序运行
        {
            Task.Delay(1000).Wait(); // 简单的等待，防止 CPU 使用率过高
        }        
    }

    private static void SendMessageToAllClients(HostData host)
    {
        byte[] sendBytes = JsonSerializer.SerializeToUtf8Bytes(host);

        foreach (var clientEndPoint in clientEndpoints)
        {
            if ((host.Mode > 1) || (host.Mode == 1 && ((DateTime.Now - lastPrintTime).TotalSeconds >= 10))
                || ((host.Content.Message == "Success") && host.Mode == 0))
            {
                Console.WriteLine($"Send to {clientEndPoint} Mode = {host.Mode} Context = {host.Content.PlayerID} {host.Content.Message}");
            }
            udpListener.Send(sendBytes, sendBytes.Length, clientEndPoint);
        }
        if ((DateTime.Now - lastPrintTime).TotalSeconds >= 10)
            lastPrintTime = DateTime.Now;
    }

    private static void StartUdpListener()
    {
        Console.WriteLine("StartUdpListener");
        Task.Run(async () =>
        {
            while (loop)
            {
                var receivedBytes = await udpListener.ReceiveAsync();
                IPEndPoint senderEndPoint = receivedBytes.RemoteEndPoint;               
                var clientData = JsonSerializer.Deserialize<ClientData>(receivedBytes.Buffer);
                ReceiveDataFromClient(clientData, senderEndPoint);  
            }
        });
    }

    public static void ReceiveDataFromClient(ClientData clientData, IPEndPoint senderEndPoint)
    {
        switch (clientData.Mode)
        {
            case -1:
                RemoveClientEndpoint(senderEndPoint);
                break;
            case 1:
                clientEndpoints.Add(senderEndPoint);
                PostbackCreationMessage(clientData.Number);
                break;
            case 2:
                UpdatePlayerPosition(clientData);
                break;
        }   
            
    }

    private static void RemoveClientEndpoint(IPEndPoint senderEndPoint)
    {
        if (clientEndpoints.Remove(senderEndPoint))
            Console.WriteLine($"{senderEndPoint} Delete Successful");
        else
            Console.WriteLine($"{senderEndPoint} Delete Failed : Unknown IP");
    }
    private static void PostbackCreationMessage(int Number)
    {
        HostData sendHostData = new HostData(0,new Content("", Number));
        if (playerList.ContainsKey(Number))       
            sendHostData.Content.Message = "Failure";                
        else
        {
            playerList[Number] = new PlayerData(new PlayerPoint(50, 50), new PlayerPoint(50, 50), 40.0, 40.0);
            sendHostData.Content.Message = "Generating";
            // 使用已存在的 private static List<Food> foods
            int batchSize = 20;
            generateFoodLock = true;
            int total = foods.Count;
            for (int i = 0; i < total; i += batchSize)
            {
                // 獲取當前批次的食物列表
                var batch = foods.Skip(i).Take(batchSize).ToList();

                // 將批次列表設置到 hostData.Content.foods
                sendHostData.Content.AddEllipse = batch;
                Console.WriteLine($"batch = {(batch.Count).ToString("D2")} To {sendHostData.Content.PlayerID} Mode = {sendHostData.Mode} Msg = {sendHostData.Content.Message}");
                // 發送當前批次的數據
                SendMessageToAllClients(sendHostData);
            }

            // 最後清空 hostData.Content.foods
            sendHostData.Content.AddEllipse = new List<Food>();
            sendHostData.Content.Message = "Success";
        }
        Console.WriteLine($"batch = XX To {sendHostData.Content.PlayerID} Mode = {sendHostData.Mode} Msg = {sendHostData.Content.Message}");
        SendMessageToAllClients(sendHostData);
        generateFoodLock = false;
    }
    private static void UpdatePlayerPosition(ClientData clientData)
    {
        if (playerList.ContainsKey(clientData.Number))
        {
            PlayerData existingData = playerList[clientData.Number];
            existingData.MousePosition = clientData.Position;
            playerList[clientData.Number] = existingData;
        }
        else
        {
            if ((DateTime.Now - lastPrintTime2).TotalSeconds >= 10)
            {
                Console.WriteLine($"Unknown mouse event Client number : {clientData.Number}");
                lastPrintTime2 = DateTime.Now;
            }
                
        }
    }
    private static void GameComputing()
    {
        Console.WriteLine("GameComputing");
        Task.Run(() =>
        {
            while (loop)
            {
                if (playerList.Count > 0)
                    Calculate();
                timeNow = DateTime.Now;
                Thread.Sleep(4); // 调整更新频率
            }
        });
    }

    private static void Calculate()
    {
        foreach (var pair in playerList)
        {
            int key = pair.Key; // 獲取鍵
            PlayerData player = pair.Value; // 獲取值
            List<Food> AddEllipse = new List<Food>();

            player.PlayerPosition = UpdatePlayerPosition(player); // 更新玩家位置

            if (!IsCreatFood)
                GenerateFood(20, AddEllipse);
            else if ((DateTime.Now - setTime).TotalSeconds >= 1) //定時投放食物
            {
                GenerateFood(5, AddEllipse);
                setTime = DateTime.Now;
            }


            playerList[key] = player;
            if (!generateFoodLock)
                hostData.Content.AddEllipse = AddEllipse;
            else
                hostData.Content.AddEllipse = new List<Food>();
            hostData.Mode = 1;
            hostData.Content.PlayerData = player;
            hostData.Content.PlayerID = key;
            hostData.Content.Message = "PlayerMove";
            SendMessageToAllClients(hostData);
        }
    }

    private static PlayerPoint UpdatePlayerPosition(PlayerData player)
    {

        double directionX = (player.MousePosition.X) - (player.PlayerPosition.X + (player.PlayerDiameter / 2));
        double directionY = (player.MousePosition.Y) - (player.PlayerPosition.Y + (player.PlayerDiameter / 2));
        double length = Math.Sqrt(directionX * directionX + directionY * directionY);


        directionX /= length; // 归一化
        directionY /= length; // 归一化
        

        PlayerPoint PP = new PlayerPoint(player.PlayerPosition.X, player.PlayerPosition.Y);

        if (length >= MinimumMovementThreshold)
        {
            PP.X += directionX * MouseSpeed;
            PP.Y += directionY * MouseSpeed;
        }
        
        //PlayerPosition.X = Math.Max(0, Math.Min(dataPacket.GameCanvasActualWidth - dataPacket.PlayerWidth, PlayerPosition.X));
        //PlayerPosition.Y = Math.Max(0, Math.Min(dataPacket.GameCanvasActualHeight - dataPacket.PlayerHeight, PlayerPosition.Y));

        return PP;
    }

    private static void GenerateFood(int qua, List<Food> AddEllipse)
    {
        if (foods.Count > initFood)
            IsCreatFood = true;
        if (foods.Count < maxFood )
        {
            int gridSize = 20; // 格子大小，根據需要調整
            int cols = (int)(map.X / gridSize);
            int rows = (int)(map.Y / gridSize);
            for (int i = 0; i < qua; i++)
            {
                int col = random.Next(cols);
                int row = random.Next(rows);

                if (!grid[col, row]) // 檢查格子是否空
                {
                    double diameter = random.Next(5, 11); // 食物直徑
                    double x = col * gridSize + (gridSize - diameter) / 2;
                    double y = row * gridSize + (gridSize - diameter) / 2;

                    int randomKey = ellipseMapPointer++;


                    int randomColor = random.Next(7);
                    Food newFood = new Food(x, y, col, row, diameter, randomColor, randomKey);
                    foods.Add(newFood);

                    //Ellipse foundEllipse = GetEllipseByKey(newFood.Key);
                    AddEllipse.Add(newFood);
                    //CreateCircle(foundEllipse, randomColor, newFood.X, newFood.Y);
                    grid[col, row] = true; // 更新格子狀態

                    // 在這裡調用創建食物的方法
                }
            }

        }
    }

}
