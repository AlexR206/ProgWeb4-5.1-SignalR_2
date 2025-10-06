import { Component } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Channel, UserEntry } from '../../models/models';
import { AuthenticationService } from 'src/app/services/authentication.service';
import * as signalR from "@microsoft/signalr";

@Component({
  selector: 'app-chat',
  templateUrl: './chat.component.html',
  styleUrls: ['./chat.component.css']
})
export class ChatComponent  {

  // Title of the page
  title = 'SignalR Chat';

  // Base URLs for API requests (not heavily used in this example)
  baseUrl = "https://localhost:7060/api/";
  accountBaseUrl = this.baseUrl + "Account/";

  // Message variables
  message: string = "test";       // Message typed by the user
  messages: string[] = [];        // All messages displayed in the chat

  // Lists received from the server
  usersList:UserEntry[] = [];     // All connected users
  channelsList:Channel[] = [];    // All available chat channels

  // Connection status flag
  isConnected: boolean = false;

  // For channel creation
  newChannelName: string = "";

  // Current selected channel or user
  selectedChannel:Channel | null = null;
  selectedUser:UserEntry | null = null;

  // The actual SignalR connection
  private hubConnection?: signalR.HubConnection

  constructor(public http: HttpClient, public authentication:AuthenticationService){}

  // Step 1: Connect to the SignalR Hub
  connectToHub() {
    // TODO: Create the connection to the hub
    this.hubConnection = new signalR.HubConnectionBuilder()
      // The token is retrieved from sessionStorage (used to identify the user)
      .withUrl('http://localhost:5106/chat', { accessTokenFactory: () => sessionStorage.getItem("token")! })
      .build();

    // 🧩 Step 2: Listen for server messages (the "on" events)

    // 1️⃣ Receive the updated user list
    this.hubConnection.on('UsersList', (data) => {
      this.usersList = data;
    });

    // 2️⃣ Receive the updated channels list
    this.hubConnection.on('ChannelsList', (data) => {
      this.channelsList = data;
    });

    // 3️⃣ Receive any new chat message
    this.hubConnection.on('NewMessage', (message) => {
      this.messages.push(message);
    });

    // 4️⃣ Receive notification when a channel was deleted (forces user to leave)
    this.hubConnection.on('LeaveChannel', (message) => {
      this.selectedChannel = null;
    });

    // Step 3: Start the connection
    this.hubConnection
      .start()
      .then(() => {
        this.isConnected = true;
        console.log("Connected to hub successfully!");
      })
      .catch(err => console.log('Error while starting connection: ' + err))
  }

  // When a user wants to start a private chat
  startPrivateChat(user: string) {
    // Invoke the server-side method
    this.hubConnection!.invoke('StartPrivateChat', user);
  }

  // When switching to a different channel
  joinChannel(channel: Channel) {
    // Leave old channel (if any) and join new one
    let selectedChannelId = this.selectedChannel ? this.selectedChannel.id : 0;
    this.hubConnection!.invoke('JoinChannel', selectedChannelId, channel.id);
    this.selectedChannel = channel;
  }

  // Sending a message (either to a channel or a private chat)
  sendMessage() {
    let selectedChannelId = this.selectedChannel ? this.selectedChannel.id : 0;
    this.hubConnection!.invoke('SendMessage', this.message, selectedChannelId, this.selectedUser?.value);
  }

  // Handle when a user is clicked
  userClick(user:UserEntry) {
    if(user == this.selectedUser){
      this.selectedUser = null; // deselect user if clicked again
    }
  }

  // Creating a new channel
  createChannel(){
    // TODO: Call the server to create a new channel
    this.hubConnection!.invoke('CreateChannel', this.newChannelName);
  }

  // Deleting a channel
  deleteChannel(channel: Channel){
    // TODO: Call the server to delete a channel
    this.hubConnection!.invoke('DeleteChannel', channel.id);
  }

  // Leaving the current channel
  leaveChannel(){
    let selectedChannelId = this.selectedChannel ? this.selectedChannel.id : 0;
    this.hubConnection!.invoke('JoinChannel', selectedChannelId, 0);
    this.selectedChannel = null;
  }
}
