﻿using Basic.Message;

namespace Arm
{
    public class HiwinArmActionFactory : ArmActionFactory
    {
        private readonly string _ip;
        private int _id;
        private bool _waiting = false;
        public int Id => _id;

        public HiwinArmActionFactory(string ip, IMessage message) : base(message)
        {
            _ip = ip;
        }

        public override Connect GetConnect()
        {
            return new HiwinConnect(_ip, _message, out _id, out _connected, ref _waiting);
        }

        public override Disconnect GetDisconnect()
        {
            return new HiwinDisconnect(_id, _message, out _connected);
        }
    }
}