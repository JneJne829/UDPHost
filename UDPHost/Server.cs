using System;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PlayerDataNamespace;
using HostDataNamespace;
using ClientDataNamespace;
using PlayerPointNamespace;
using System.Collections.Concurrent;

public class UdpServer
{
    private static HashSet<IPEndPoint> clientEndpoints = new HashSet<IPEndPoint>();
    private static ConcurrentDictionary<int, PlayerData> playerList = new ConcurrentDictionary<int, PlayerData>();
    private static IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
    private static UdpClient udpListener = new UdpClient(11000);
    private static HostData hostData = new(0, new Content("", 0));
    private const double MinimumMovementThreshold = 5.0;
    private static double MouseSpeed = 3;
    private static bool loop = true;
    private static DateTime lastPrintTime = DateTime.MinValue;
    private static DateTime lastPrintTime2 = DateTime.MinValue;
    private static DateTime timeNow = DateTime.Now;
    

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
            if ((host.Mode != 1) || (host.Mode == 1 && ((DateTime.Now - lastPrintTime).TotalSeconds >= 10)))
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
        if (!playerList.ContainsKey(Number))
        {
            playerList[Number] = new PlayerData(new PlayerPoint(50, 50), new PlayerPoint(50, 50), 40.0, 40.0);
            hostData.Content.Message = "Success";
        }
        else
            hostData.Content.Message = "Failure";
        hostData.Content.PlayerID = Number;
        hostData.Mode = 0;
        SendMessageToAllClients(hostData);
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
            if ((DateTime.Now - lastPrintTime).TotalSeconds >= 10)
            {
                Console.WriteLine($"Unknown mouse event Client number : {clientData.Number}");
                lastPrintTime = DateTime.Now;
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

            player.PlayerPosition = UpdatePlayerPosition(player); // 更新玩家位置

            playerList[key] = player;

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

}
