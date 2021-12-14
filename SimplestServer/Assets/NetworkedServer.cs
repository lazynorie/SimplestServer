using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;
    
    LinkedList<PlayerAccount> playerAccounts;

    string playeraccountfilepath;

    private int playerWaitingForMatch = -1;

    private LinkedList<GameSession> _gameSessions;

    private GameSessionManager _gameSessionManager;

    // Start is called before the first frame update
    void Start()
    {
        playeraccountfilepath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccountData.txt";
        
        NetworkTransport.Init();
        
        ConnectionConfig config = new ConnectionConfig();

        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);

        HostTopology topology = new HostTopology(config, maxConnections);

        hostID = NetworkTransport.AddHost(topology, socketPort, null);
        
        playerAccounts = new LinkedList<PlayerAccount>();
        _gameSessions = new LinkedList<GameSession>();
        _gameSessionManager = new GameSessionManager();
        
        LoadPlayerAccounts();
        
    }

    // Update is called once per frame
    void Update()
    {
        //bool hasNothing = false;


        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];//this is for messages
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }

    }
  
    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }
    
    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);

        //split the msg
        string[] csv = msg.Split(',');

        int singifier = int.Parse(csv[0]);

        if (singifier == ClientToServerSignifiers.CreateAccount)
        {
            string n = csv[1];
            string p = csv[2];

            bool isUnique = true;
            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    isUnique = false;
                    break;
                }
            }
            
            if (isUnique)
            {
                playerAccounts.AddLast(new PlayerAccount(n, p));
                SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.Success + "," + n, id);
                
                //Save player account list!
                SavePlayerAccounts();
            }

            else
            {
                SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.FailureNameInUse, id);
 
            }
        }
        else if (singifier == ClientToServerSignifiers.Login)
        {
            string n = csv[1];
            string p = csv[2];

            bool hasBeenFound = false;
            //bool responseHasBeenSent = false;
            
            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    if (pa.password == p)
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.Success + "," + n, id);
                        //bool responseHasBeenSent = true;

                    }
                    else
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.FailureIncorrectPassword, id);
                        //bool responseHasBeenSent = true;

                    }

                    //we have found players account! do something
                    
                    hasBeenFound = true;
                    break;
                }
            }

            if (!hasBeenFound)
            {
                SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.FailureNameInNotFound, id);

            }
        }
        else if (singifier == ClientToServerSignifiers.AddToGameSessionQueue)
        {
            //if there is no player waiting, save the waiting player in the above variable

            if (playerWaitingForMatch == -1)
            {
                playerWaitingForMatch = id;
            }
            else
            {
                int gameroomID = GameSessionManager.GetGameSessionIDnumber();
                GameSession gs = new GameSession(playerWaitingForMatch, id, gameroomID);
                //_gameSessions.AddLast(gs);
                _gameSessionManager.allGameSession.Add(gs);
                

                int playerWaitingForMatchMovesFirst = Random.Range(0, 2);
                int currentPlayersMove = (playerWaitingForMatchMovesFirst == 1)?0:1;
                //[0]signifier [1]who moves first [2]gameroomID
                SendMessageToClient(string.Join(",",ServerToClientSignifiers.GameSessionStarted.ToString(), playerWaitingForMatchMovesFirst,gameroomID.ToString()), playerWaitingForMatch);
                SendMessageToClient(string.Join(",",ServerToClientSignifiers.GameSessionStarted.ToString(), currentPlayersMove,gameroomID.ToString()), id);
                
                playerWaitingForMatch = -1;
            }
        }
        /*else if (singifier == ClientToServerSignifiers.TicTacToePlay)
        {
            //
            Debug.Log("let's play!");

            GameSession gs = FindGameSessionWithPlayerID(id);

            if (gs.playerID1 == id)
                SendMessageToClient(ServerToClientSignifiers.OpponentTicTacToePlay + "", gs.playerID2);
            else
                SendMessageToClient(ServerToClientSignifiers.OpponentTicTacToePlay + "", gs.playerID1);
        }*/
        else if (singifier ==  ClientToServerSignifiers.TicTacToePlayMade)
        {
            //csv[0]siginifier  csv[1]which button   csv[2]change my turn  csv[3]tell server it's opponent's turn
            GameSession gs = FindGameSessionWithPlayerID(id);
            if (gs != null)
            {
                if (gs.playerID1 ==id)
                {
                    SendMessageToClient(string.Join(",",ServerToClientSignifiers.OpponentTicTacToePlay.ToString(),csv[1],csv[2],csv[3]),gs.playerID2);
                }
                else
                {
                    SendMessageToClient(string.Join(",",ServerToClientSignifiers.OpponentTicTacToePlay.ToString(),csv[1],csv[2],csv[3]),gs.playerID1); 
                }
                foreach (int observerID in gs.observerIDs)
                {
                    SendMessageToClient(string.Join(",", ServerToClientSignifiers.UpDateOB.ToString(), csv[1], csv[2],csv[3]), observerID);
                }
            }
            
        }
        else if (singifier == ClientToServerSignifiers.PlayerMessage)
        {
            GameSession gs = FindGameSessionWithPlayerID(id);
            if (gs!=null)
            {
                if (gs.playerID1 == id)
                {
                    SendMessageToClient(string.Join(",",ServerToClientSignifiers.SendChatToOpponent.ToString(),csv[1],csv[2]),gs.playerID2);
                }
                else
                {
                    SendMessageToClient(string.Join(",",ServerToClientSignifiers.SendChatToOpponent.ToString(),csv[1],csv[2]),gs.playerID1);
                }
                foreach (int observerNum in gs.observerIDs)
                {
                    if (observerNum != id)
                        SendMessageToClient(string.Join(",", ServerToClientSignifiers.SendChatToOpponent.ToString(), csv[1], csv[2]), observerNum);
                }
            }
            else
            {
                gs = FindGameSessionWithObserverID(id);
                if (gs != null)
                {
                    SendMessageToClient(string.Join(",", ServerToClientSignifiers.SendChatToOpponent.ToString(), csv[1], csv[2]), gs.playerID1);
                    SendMessageToClient(string.Join(",", ServerToClientSignifiers.SendChatToOpponent.ToString(), csv[1], csv[2]), gs.playerID2);

                    foreach(int observerNum in gs.observerIDs)
                    {
                        if (observerNum != id)
                            SendMessageToClient(string.Join(",", ServerToClientSignifiers.SendChatToOpponent.ToString(), csv[1], csv[2]), observerNum);

                    }
                }

            }
        }
        else if (singifier == ClientToServerSignifiers.WinMsg)
        {
            GameSession gs = FindGameSessionWithPlayerID(id);
            if (gs!=null)
            {
                if (gs.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.GGMsg.ToString(),gs.playerID2);
                }
                else
                {
                    SendMessageToClient(ServerToClientSignifiers.GGMsg.ToString(),gs.playerID1);
                }
            }
        }
        else if (singifier == ClientToServerSignifiers.GameDraw)
        {
            GameSession gs = FindGameSessionWithPlayerID(id);
            if (gs!=null)
            {
                if (gs.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.DrawMsg.ToString(),gs.playerID2);
                }
                else
                {
                    SendMessageToClient(ServerToClientSignifiers.DrawMsg.ToString(),gs.playerID1);
                }
               
                foreach(int observerNum in gs.observerIDs)
                {
                    if (observerNum != id) 
                        SendMessageToClient(string.Join(",", ServerToClientSignifiers.DrawMsg.ToString()), observerNum);
                }
            }
        }
        else if (singifier == ClientToServerSignifiers.TicTacToePlay)
        {
            int GameRoomID = int.Parse(csv[1]);
            foreach (GameSession gameSession in _gameSessionManager.allGameSession)
            {
                if (GameRoomID == gameSession.roomID)
                {
                    SendMessageToClient(string.Join(",",ServerToClientSignifiers.OBrequestRecieved),id);
                    gameSession.observerIDs.Add(id);
                }
            }
        }
        else if (singifier == ClientToServerSignifiers.OBrequestSent)
        {
            int GameRoomID = int.Parse(csv[1]);
            foreach (GameSession gs in _gameSessionManager.allGameSession)
            {
                if (GameRoomID == gs.roomID)
                {
                    SendMessageToClient(string.Join(",",ServerToClientSignifiers.OBrequestRecieved),id);
                    //to get 
                    SendMessageToClient(string.Join(",",ServerToClientSignifiers.UpdateCurrentBoardToOB.ToString(),id.ToString()),gs.playerID1);
                    gs.observerIDs.Add(id);
                    return;
                }
            }
        }
       
    }
    
    private void SavePlayerAccounts()
    {
        StreamWriter sw = new StreamWriter(playeraccountfilepath);


        foreach (PlayerAccount pa in playerAccounts)
        {
            sw.WriteLineAsync(pa.name + "," + pa.password);
        }
        sw.Close();
    }
    
    private void LoadPlayerAccounts()
    {
        if (File.Exists(playeraccountfilepath))
        {
            StreamReader sr = new StreamReader(playeraccountfilepath);
            string line;
            while((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');

                PlayerAccount pa = new PlayerAccount(csv[0], csv[0]);
                playerAccounts.AddLast(pa);
            }
        }
       
    }

    private GameSession FindGameSessionWithPlayerID(int id)
    {
        foreach (GameSession gs in _gameSessionManager.allGameSession)
        {
            if (gs.playerID1 == id || gs.playerID2 == id)
                return gs;
        }

        return null;
    }

    private GameSession FindGameSessionWithObserverID(int id)
    {
        foreach (GameSession gs in _gameSessionManager.allGameSession)
        {
            foreach (int observerID in gs.observerIDs)
            {
                if (observerID == id)
                    return gs;
            }
        }

        return null;
    }

   
}

public class GameSession
{
    public int playerID1, playerID2, roomID;

    public List<int> observerIDs;
    
    public GameSession(int PlayerID1, int PlayerID2, int RoomID)
    {
        playerID1 = PlayerID1;
        playerID2 = PlayerID2;
        roomID = RoomID;

        observerIDs = new List<int>();
    }
    //Hold two clients
    //to do work item
    //... but we are working to do it, with plan of coming back once we have a better understanding of whats going on and what we must do
}

public class GameSessionManager
{
    public static int nextGameSessionIDNumber = 0;
    public static int GetGameSessionIDnumber()
    {
        nextGameSessionIDNumber++;
        return nextGameSessionIDNumber;
    }
    public List<GameSession> allGameSession;
    public GameSessionManager()
    {
        allGameSession = new List<GameSession>();
    }
}




//set up account class
public class PlayerAccount
{
    public string name, password;
    
    public PlayerAccount(string Name, string Password)
    {
        name = Name;
        password = Password;
    }
}

public static class ClientToServerSignifiers
{
    public const int Login = 1;
    public const int CreateAccount = 2;
    public const int AddToGameSessionQueue = 3;
    public const int TicTacToePlay = 4;
    public const int PlayerMessage = 5;
    public const int TicTacToePlayMade = 6;
    public const int WinMsg = 7;
    public const int OBrequestSent = 8;
    public const int GameDraw = 9;
   
    

}

public static class ServerToClientSignifiers
{
    public const int LoginResponse = 1;
    public const int GameSessionStarted = 2;
    public const int OpponentTicTacToePlay = 3;
    public const int SendChatToOpponent = 4;
    public const int PlayerDC = 5;
    public const int GGMsg = 6;
    public const int OBrequestRecieved = 8;
    public const int UpdateCurrentBoardToOB = 9;
    public const int UpDateOB = 10;
    public const int DrawMsg = 11;
    
}

public static class LoginResponses
{
    public const int Success = 1;

    public const int FailureNameInUse = 2;
    
    public const int FailureNameInNotFound = 3;

    public const int FailureIncorrectPassword = 4;
}

