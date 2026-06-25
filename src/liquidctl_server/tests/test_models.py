from liquidctl_server.models import Mode


class TestModeIsSlave:
    def test_one_is_slave(self):
        assert Mode.is_slave(1) is True

    def test_five_is_slave(self):
        assert Mode.is_slave(5) is True

    def test_zero_is_not_slave(self):
        assert Mode.is_slave(0) is False

    def test_two_is_not_slave(self):
        assert Mode.is_slave(2) is False

    def test_three_is_not_slave(self):
        assert Mode.is_slave(3) is False


class TestModeIsMaster:
    def test_zero_is_master(self):
        assert Mode.is_master(0) is True

    def test_four_is_master(self):
        assert Mode.is_master(4) is True

    def test_one_is_not_master(self):
        assert Mode.is_master(1) is False

    def test_two_is_not_master(self):
        assert Mode.is_master(2) is False

    def test_three_is_not_master(self):
        assert Mode.is_master(3) is False
