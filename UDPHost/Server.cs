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
using RankMemberNamespace;
using System.Linq;

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
    private static Point map = new Point(6000,6000);
    private const double MinimumMovementThreshold = 5.0;
    private static double MouseSpeed = 3;
    private const int initFood = 7000;
    private const int maxFood = 10000;
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
            if ((host.Mode > 1 && host.Mode < 4) || (host.Mode == 1 && ((DateTime.Now - lastPrintTime).TotalSeconds >= 10))
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
                //讀取到訊息後儲存IP、返序列化並調用ReceiveDataFromClient()處理資料
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
                PostbackCreationMessage(clientData.Name, clientData.Number, clientData.Color);
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
    private static void PostbackCreationMessage(String name, int Number, int color)
    {
        HostData sendHostData = new HostData(0,new Content("", Number));
        if (playerList.ContainsKey(Number))       
            sendHostData.Content.Message = "Failure";                
        else
        {
            Point rebirthPoint = new Point(random.Next(100, map.X - 100), random.Next(100, map.Y - 100));
            playerList[Number] = new PlayerData(name, new PlayerPoint(rebirthPoint.X, rebirthPoint.Y), new PlayerPoint(rebirthPoint.X, rebirthPoint.Y), 20.0, CalculatePlayerDiameter(20.0), color, 0, 0);
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

        List<RankMember> rank = new List<RankMember>();

        foreach (var pair in playerList)
        {
            int key = pair.Key; // 獲取鍵
            PlayerData player = pair.Value; // 獲取值
            List<Food> AddEllipse = new List<Food>();
            List<int> eatenFood = new List<int>();

            CheckFoodProximity(player,eatenFood);
            player.PlayerPosition = UpdatePlayerPosition(player); // 更新玩家位置

            if (!generateFoodLock)
            {
                if (!IsCreatFood)
                    GenerateFood(20, AddEllipse);
                else if ((DateTime.Now - setTime).TotalSeconds >= 1) //定時投放食物
                {
                    GenerateFood(20, AddEllipse);
                    setTime = DateTime.Now;
                }
            }

            rank.Add(new RankMember(player.PlayerName, player.PlayerMass));
            playerList[key] = player;
            hostData.Content.AddEllipse = AddEllipse;
            hostData.Content.eatenFood = eatenFood;
            ///hostData.Content.PlayerData.PlayerMass = pair.Value.PlayerMass;
            hostData.Mode = 1;
            hostData.Content.PlayerData = player;
            hostData.Content.PlayerID = key;
            hostData.Content.Message = "PlayerMove";
            SendMessageToAllClients(hostData);
        }

        rank = rank.OrderByDescending(r => r.Mass).ToList();
        HostData Rankdata = new HostData(4, new Content { Rank = rank });
        SendMessageToAllClients(Rankdata); // 傳送排名

        List<int> deleteKey = new List<int>();
        foreach (var pair1 in playerList) // 計算玩家碰撞
        {
            foreach (var pair2 in playerList)
            {
                if (pair1.Key != pair2.Key) // 确保不与自己比较
                {
                    double distance = CalculateDistance(pair1.Value.PlayerPosition, pair2.Value.PlayerPosition, pair1.Value.PlayerDiameter, pair2.Value.PlayerDiameter);

                    if (distance <= (pair1.Value.PlayerDiameter / 2) && (pair2.Value.PlayerMass / pair1.Value.PlayerMass) < 0.7)
                    {
                        pair1.Value.PlayerMass += pair2.Value.PlayerMass * 0.9;
                        pair1.Value.EatenPlayer += 1;
                        pair1.Value.PlayerDiameter = CalculatePlayerDiameter(pair1.Value.PlayerMass);
                        deleteKey.Add(pair2.Key);
                    }
                }

            }
        }
       
        foreach (int key in deleteKey)
        {
            hostData.Mode = 1;
            hostData.Content.Message = "Delete";
            hostData.Content.PlayerID = key;
            SendMessageToAllClients(hostData);
            playerList.TryRemove(key, out _);
        }
    }
    private static double CalculateDistance(PlayerPoint point1, PlayerPoint point2, double pair1D, double pair2D)
    {
        double xDiff = (point1.X + (pair1D / 2)) - (point2.X + (pair2D / 2));
        double yDiff = (point1.Y + (pair1D / 2)) - (point2.Y + (pair2D / 2));
        return Math.Sqrt(xDiff * xDiff + yDiff * yDiff);
    }
    private static void CheckFoodProximity(PlayerData player, List<int> eatenFood)
    {
        // 获取距离小于10的食物列表
        var closeFoods = foods
            .Where(food => CalculateDistance(player.PlayerPosition.X + (player.PlayerDiameter / 2), player.PlayerPosition.Y + player.PlayerDiameter / 2, food.X + (food.Diameter / 2), food.Y + food.Diameter / 2) < (player.PlayerDiameter / 2) + (food.Diameter / 2) * 0.8)
            .ToList();
        int EatenFood = 0;
        // 对于每个靠近的食物，执行某种操作
        foreach (var food in closeFoods)
        {
            // 执行您想要的回饋逻辑
            EatenFood += 1;
            player = GiveFeedback(player, food);
            eatenFood.Add(food.Key);
        }
        player.EatenFood = EatenFood;
    }
    public static PlayerData GiveFeedback(PlayerData player, Food food)
    {
        double coe = CalculateCoe(player.PlayerMass);
        player.PlayerMass += food.Diameter / coe;
        player.PlayerDiameter = CalculatePlayerDiameter(player.PlayerMass); //這裡可以改變質量映射的直徑
        grid[food.Col, food.Row] = false;
        foods.Remove(food);
        return player;
    }
    public static double CalculatePlayerDiameter(double playerMass)
    {
        // 根據質量計算黏性，這裡可以根據您的遊戲邏輯來調整
        double stickiness = 1 - Math.Log10(playerMass) / 10;

        // 使用計算出的黏性來確定直徑
        return Math.Pow(playerMass, stickiness);
    }
    private static double CalculateCoe(double playerMass)
    {
        if (playerMass < 30)
            return 10.0;
        else if (playerMass < 50)
            return 1.2 * CalculateCoe(playerMass - 10);
        else if (playerMass < 100)
            return 1.2 * CalculateCoe(playerMass - 10);
        else
            return 1.3 * CalculateCoe(playerMass - 100);
    }
    public static double CalculateDistance(double x1, double y1, double x2, double y2)
    {
        return Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
    }

    private static PlayerPoint UpdatePlayerPosition(PlayerData player)
    {

        double directionX = (player.MousePosition.X) - (player.PlayerPosition.X + (player.PlayerDiameter / 2));
        double directionY = (player.MousePosition.Y) - (player.PlayerPosition.Y + (player.PlayerDiameter / 2));
        double length = Math.Sqrt(directionX * directionX + directionY * directionY);


        directionX /= length; // 归一化
        directionY /= length; // 归一化
        

        PlayerPoint PP = new PlayerPoint(player.PlayerPosition.X, player.PlayerPosition.Y);

        if (length >= player.PlayerDiameter / 2)
        {
            PP.X += directionX * MouseSpeed * CalculationSpeedCoe(player.PlayerMass);
            PP.Y += directionY * MouseSpeed * CalculationSpeedCoe(player.PlayerMass);
        }
        else if(length >= player.PlayerDiameter / 4)
        {
            PP.X += directionX * MouseSpeed * CalculationSpeedCoe(player.PlayerMass) * (length / player.PlayerDiameter / 1.5);
            PP.Y += directionY * MouseSpeed * CalculationSpeedCoe(player.PlayerMass) * (length / player.PlayerDiameter / 1.5);
        }
        else if (length >= player.PlayerDiameter / 6)
        {
            PP.X += directionX * MouseSpeed * CalculationSpeedCoe(player.PlayerMass) * (length / player.PlayerDiameter / 2);
            PP.Y += directionY * MouseSpeed * CalculationSpeedCoe(player.PlayerMass) * (length / player.PlayerDiameter / 2);
        }
        else if (length >= player.PlayerDiameter / 12)
        {
            PP.X += directionX * MouseSpeed * CalculationSpeedCoe(player.PlayerMass) * (length / player.PlayerDiameter / 3);
            PP.Y += directionY * MouseSpeed * CalculationSpeedCoe(player.PlayerMass) * (length / player.PlayerDiameter / 3);
        }

        //PlayerPosition.X = Math.Max(0, Math.Min(dataPacket.GameCanvasActualWidth - dataPacket.PlayerWidth, PlayerPosition.X));
        //PlayerPosition.Y = Math.Max(0, Math.Min(dataPacket.GameCanvasActualHeight - dataPacket.PlayerHeight, PlayerPosition.Y));

        return PP;
    }
    public static double CalculationSpeedCoe(double playerMass)
    {
        const double maxMass = 400;
        const double minCoe = 0.6;
        const double transitionMass = 100; // 在此质量值之前变化较慢
        const double transitionCoe = 0.92; // 在 transitionMass 时的系数

        // 确保质量在1和400之间
        playerMass = Math.Max(1, Math.Min(playerMass, maxMass));

        double speedCoe;
        if (playerMass <= transitionMass)
        {
            // 在 transitionMass 之前使用较慢的变化速度
            double rangeToTransition = 1 - transitionCoe;
            double normalizedMass = playerMass / transitionMass;
            speedCoe = 1 - (rangeToTransition * normalizedMass);
        }
        else
        {
            // 在 transitionMass 之后使用更快的变化速度
            double rangePostTransition = transitionCoe - minCoe;
            double normalizedMass = (playerMass - transitionMass) / (maxMass - transitionMass);
            speedCoe = transitionCoe - (rangePostTransition * normalizedMass);
        }

        return speedCoe;
    }
    private static void GenerateFood(int qua, List<Food> AddEllipse)
    {
        if (foods.Count > initFood)
            IsCreatFood = true;
        if (foods.Count < maxFood )
        {
            int gridSize = 5; // 格子大小，根據需要調整
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
